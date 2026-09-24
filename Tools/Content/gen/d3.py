#!/usr/bin/env python3
"""Генератор части d3 направления DevOps (middle): Kubernetes, Helm, дебаг подов, Terraform, Ansible.

python3 gen/d3.py  ->  out/devops_d3.json
"""
import json
import os
import random

OUT = "Tools/Content/out/devops_d3.json"

XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}
XP_MULT = {"intern": 1.0, "junior": 1.0, "junior_plus": 1.25, "middle": 1.5}


def xp(d, g="middle"):
    return int(round(XP_BASE[d] * XP_MULT[g] / 5.0)) * 5


def blk(key, inner):
    """Регулярка: внутри YAML-блока `key:` (по отступам) встречается `inner`.
    Работает и для блочного стиля, и для flow-стиля в одну строку ({cpu: 100m, memory: 256Mi})."""
    return (r"^( *)(?:- )?" + key + r":(?:[^\n]*\n(?:\1[ -][^\n]*\n)*?\1[ -])?[^\n]*" + inner)


# Тело HCL-блока до его закрывающей скобки, с вложенными блоками до двух уровней
# (validation { }, default_tags { tags = { } }). В регулярках пишется как <B>.
HB = r"(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*"


def rx(desc, regex, flags="m"):
    return {"input": desc, "expected": {"regex": regex.replace("<B>", HB), "flags": flags}}


def nrx(desc, regex, flags="m"):
    return {"input": desc, "expected": {"not_regex": regex.replace("<B>", HB), "flags": flags}}


TASKS = []


def task(task_id, topic_id, typ, difficulty, character, story, question, explanation, hints,
         code=None, language=None, options=None, correct_answer=None, test_cases=None, time_limit=None):
    if options is not None and correct_answer is not None:
        # Детерминированно перемешиваем варианты, чтобы верный ответ не стоял всегда на одном месте
        perm = list(range(len(options)))
        random.Random("d3:" + task_id).shuffle(perm)
        options = [options[i] for i in perm]
        if isinstance(correct_answer, list):
            correct_answer = sorted(perm.index(i) for i in correct_answer)
        else:
            correct_answer = perm.index(correct_answer)
    TASKS.append({
        "task_id": task_id,
        "topic_id": topic_id,
        "grade": "middle",
        "type": typ,
        "difficulty": difficulty,
        "xp_reward": xp(difficulty),
        "time_limit_minutes": time_limit,
        "character": character,
        "story": story,
        "content": {
            "question": question,
            "code": code,
            "language": language,
            "options": options,
            "correct_answer": correct_answer,
            "test_cases": test_cases,
        },
        "explanation": explanation,
        "hints": hints,
    })


TOPICS = []

# =====================================================================
# do-k8s-basics
# =====================================================================
TOPICS.append({"topic_id": "do-k8s-basics", "theory": """Kubernetes работает декларативно: ты описываешь в YAML желаемое состояние («3 реплики каталога версии 1.14.2»), а контроллеры кластера постоянно сверяют его с реальностью и чинят расхождения. Упал под — поднимут новый, умерла нода — переселят поды на другие.

Главные объекты:
• Pod — один или несколько контейнеров с общим IP и томами. Руками поды не создают: если такой под умрёт вместе с нодой, его никто не пересоздаст.
• Deployment — «держи N реплик вот такого шаблона пода и обновляй их по очереди». Для каждой версии шаблона он создаёт ReplicaSet и хранит старые для отката (kubectl rollout undo).
• Service — постоянное DNS-имя и виртуальный IP для группы подов. Поды он ищет по лейблам и шлёт трафик только в те, что прошли readiness. Типы: ClusterIP (по умолчанию, внутри кластера), NodePort, LoadBalancer.
• ConfigMap — настройки вне образа: подключаются как переменные окружения (env, envFrom) или как файлы.

Всё связано лейблами. У Deployment spec.selector.matchLabels должен совпадать с template.metadata.labels, у Service spec.selector — с лейблами подов, причём все пары сразу (логическое И). Опечатка в одной букве — и у Service пустые endpoints, клиенты получают 503. Порты Service: port — где слушает сам Service, targetPort — порт, который реально слушает приложение в контейнере.

Пробы:
• readinessProbe — готов ли под принимать трафик. Не прошла — под убирают из балансировки, но не перезапускают.
• livenessProbe — жив ли процесс. Не прошла failureThreshold раз подряд — kubelet перезапускает контейнер. Проверяй только сам процесс: если liveness ходит в базу, сбой базы перезапустит все поды разом.
• startupProbe — для медленного старта: пока она не прошла, liveness и readiness не проверяются.

Ресурсы:
• requests — сколько зарезервировать. По requests планировщик выбирает ноду, реальное потребление он не смотрит.
• limits — потолок. Упёрся в limit CPU — троттлинг (работает, но медленнее), превысил limit памяти — контейнер убивают (OOMKilled).
• QoS-класс: requests = limits у всех контейнеров — Guaranteed, задано хоть что-то — Burstable, ничего — BestEffort; такие поды вытесняются первыми, когда на ноде кончается память.

Фрагмент шаблона пода:

    containers:
      - name: catalog
        image: registry.kodzilla.ru/market/catalog:1.14.2
        ports:
          - containerPort: 8000
        readinessProbe:
          httpGet:
            path: /ready
            port: 8000
        resources:
          requests:
            cpu: 100m
            memory: 256Mi
          limits:
            memory: 512Mi

Тег latest не используй: непонятно, что крутится, а откат вернёт тот же latest. Значения в data у ConfigMap — только строки ("300", "true"). Переменные окружения читаются при старте контейнера, поэтому после правки ConfigMap поды надо перезапустить: kubectl rollout restart deployment/catalog.

Команды на каждый день: kubectl apply -f, kubectl get pods -l app=catalog --show-labels, kubectl describe svc catalog, kubectl get endpointslices -l kubernetes.io/service-name=catalog, kubectl rollout status deployment/catalog, kubectl explain deployment.spec."""})

T = "do-k8s-basics"

task(
    "do-k8s-basics-01", T, "write_code", 3, "devops_colleague",
    "Дима: «Каталог Маркета на стейдже до сих пор запущен голым подом, который кто-то когда-то создал руками. Ночью ноду перезагрузили — каталог так и не поднялся, и Ира полдня не могла ничего тестировать. Сделай по-человечески, чтобы такое больше не повторялось».",
    "Перепиши манифест в Deployment catalog (apps/v1): 3 реплики; selector и лейблы подов app: catalog; образ с тегом 1.14.2 вместо latest; readinessProbe — HTTP GET /ready на порт 8000; livenessProbe — HTTP GET /healthz на порт 8000; requests: cpu 100m и memory 256Mi, limit памяти 512Mi.",
    "Голый под никто не пересоздаёт: он привязан к ноде, и с ней умирает. Deployment через ReplicaSet держит заданное число реплик и при потере ноды поднимет поды на других. Селектор обязан совпадать с лейблами шаблона, иначе API отклонит манифест. readiness и liveness разделены: /ready решает, пускать ли на под трафик, /healthz — жив ли процесс, и перезапуск только по второй. requests нужны планировщику, чтобы не поселить под на переполненную ноду, а limit памяти защищает соседей от утечки. CPU-лимит тут сознательно не задан: он даёт троттлинг, а память без лимита опасна для всей ноды.",
    [
        "Начни с apiVersion: apps/v1 и kind: Deployment, а старый под целиком перенеси в spec.template.",
        "spec.selector.matchLabels и spec.template.metadata.labels должны содержать одинаковый app: catalog.",
        "Пробы и resources пишутся внутри контейнера: readinessProbe.httpGet.path, livenessProbe.httpGet.path, resources.requests и resources.limits.",
    ],
    code="""apiVersion: v1
kind: Pod
metadata:
  name: catalog
  labels:
    app: catalog
spec:
  containers:
    - name: catalog
      image: registry.kodzilla.ru/market/catalog:latest
      ports:
        - containerPort: 8000
""",
    language="yaml",
    correct_answer="""apiVersion: apps/v1
kind: Deployment
metadata:
  name: catalog
  labels:
    app: catalog
spec:
  replicas: 3
  selector:
    matchLabels:
      app: catalog
  template:
    metadata:
      labels:
        app: catalog
    spec:
      containers:
        - name: catalog
          image: registry.kodzilla.ru/market/catalog:1.14.2
          ports:
            - containerPort: 8000
          readinessProbe:
            httpGet:
              path: /ready
              port: 8000
            periodSeconds: 5
          livenessProbe:
            httpGet:
              path: /healthz
              port: 8000
            initialDelaySeconds: 10
            periodSeconds: 10
            failureThreshold: 3
          resources:
            requests:
              cpu: 100m
              memory: 256Mi
            limits:
              memory: 512Mi
""",
    test_cases=[
        rx("Это Deployment из apps/v1", r"(?=[\s\S]*^apiVersion:\s*apps/v1\s*$)[\s\S]*^kind:\s*Deployment\s*$"),
        rx("3 реплики", r"^\s*replicas:\s*3\s*$"),
        rx("selector.matchLabels: app: catalog", blk("matchLabels", r"app:\s*['\"]?catalog['\"]?\s*[,}]?\s*$")),
        rx("Лейбл app: catalog в шаблоне пода", r"^\s*template:[\s\S]*^\s*labels:\s*\{?[^\n]*\n?[\s\S]*?app:\s*['\"]?catalog\b"),
        rx("Образ с тегом 1.14.2", r"^\s*(?:- )?image:\s*['\"]?registry\.kodzilla\.ru/market/catalog:1\.14\.2['\"]?\s*$"),
        nrx("Нет тега latest", r":latest\b"),
        rx("readinessProbe ходит на /ready", blk("readinessProbe", r"path:\s*['\"]?/ready\b")),
        rx("livenessProbe ходит на /healthz", blk("livenessProbe", r"path:\s*['\"]?/healthz\b")),
        rx("requests: cpu 100m", blk("requests", r"cpu:\s*['\"]?100m\b")),
        rx("requests: memory 256Mi", blk("requests", r"memory:\s*['\"]?256Mi\b")),
        rx("limits: memory 512Mi", blk("limits", r"memory:\s*['\"]?512Mi\b")),
    ],
)

task(
    "do-k8s-basics-02", T, "find_bug", 3, "qa",
    "Ира: «На стейдже не грузятся отзывы: фронт получает 503 от gateway на /api/reviews. Поды reviews в статусе Running, рестартов нет, но в их логах тишина — ни одного запроса. Шаги: открыть любую карточку товара → блок отзывов пустой, в Network 503».",
    "Почему запросы не доходят до подов reviews? Выбери верный диагноз и исправление.",
    "Service находит поды только по лейблам, и нужно совпадение всех пар из selector сразу. У подов app: reviews, а Service ищет app: review, поэтому ему не подходит ни один под, список endpoints пуст, и прокси отвечает 503. Быстрая проверка: kubectl get pods -l app=review,tier=backend вернёт пусто, а kubectl describe svc покажет Endpoints: <none>. Два лейбла в селекторе — это нормально, targetPort 8000 совпадает с containerPort, а без readinessProbe под считается готовым сразу после старта контейнера. Тип ClusterIP доступен всем подам кластера из любого namespace, LoadBalancer нужен только для выхода наружу.",
    [
        "Посмотри на строку Endpoints: <none> — Service не нашёл ни одного пода.",
        "Сравни посимвольно selector у Service и labels в шаблоне пода.",
    ],
    code="""apiVersion: apps/v1
kind: Deployment
metadata:
  name: reviews
spec:
  replicas: 2
  selector:
    matchLabels:
      app: reviews
      tier: backend
  template:
    metadata:
      labels:
        app: reviews
        tier: backend
    spec:
      containers:
        - name: reviews
          image: registry.kodzilla.ru/market/reviews:2.3.0
          ports:
            - containerPort: 8000
---
apiVersion: v1
kind: Service
metadata:
  name: reviews
spec:
  selector:
    app: review
    tier: backend
  ports:
    - port: 80
      targetPort: 8000
# $ kubectl describe svc reviews | grep -E 'Selector|Endpoints'
# Selector:          app=review,tier=backend
# Endpoints:         <none>
""",
    language="yaml",
    options=[
        "targetPort должен совпадать с port Service: трафик приходит на 80 и уходит на 8000, где его не ждут, — поставить targetPort: 80",
        "selector Service ищет app: review, а у подов app: reviews — ни один под не подходит. Исправить selector на app: reviews",
        "С двумя лейблами в selector Service ищет поды по ИЛИ и путается в endpoints — оставить только app: reviews",
        "Нет readinessProbe: без неё kubelet не отмечает поды готовыми, и они никогда не попадают в endpoints",
        "ClusterIP доступен только подам своего namespace, а gateway живёт в другом — нужен type: LoadBalancer",
    ],
    correct_answer=1,
)

task(
    "do-k8s-basics-03", T, "quiz", 4, "teamlead",
    "Марина на ревью инфраструктурного PR: «Ты везде копируешь limits из requests, а потом удивляешься, что поиск тормозит при полупустых нодах, а поды без ресурсов вылетают первыми. Давай проверим, как ты понимаешь ресурсы, прежде чем я это смержу».",
    "Выбери все верные утверждения про requests и limits.",
    "Планировщик смотрит только на requests: сумма запросов подов на ноде не может превысить её allocatable, а фактическое потребление при выборе ноды не учитывается. Лимиты ведут себя по-разному: CPU — ресурс сжимаемый, при упоре в limit ядро просто троттлит процесс, а память несжимаемая — превысил limit, и контейнер убивают с OOMKilled. Поды вообще без requests и limits получают QoS BestEffort и вытесняются первыми при нехватке памяти на ноде. Если limit не указан, он не равен request — контейнер может занять всю свободную память ноды (разве что в namespace есть LimitRange с умолчаниями). По limits Kubernetes разрешает overcommit: их сумма может быть больше памяти ноды, поэтому щедрые limits без разумных requests — риск для всей ноды.",
    [
        "Какой ресурс можно «притормозить», а какой нельзя отобрать у процесса без убийства?",
        "Что именно проверяет планировщик, когда ищет ноду для пода?",
        "Подумай, куда деваются поды без ресурсов, когда нода испытывает memory pressure.",
    ],
    options=[
        "Планировщик выбирает ноду по requests подов, а не по их фактическому потреблению",
        "Если контейнер упирается в limits.cpu, ядро убивает его, и в статусе будет OOMKilled",
        "Превышение limits.memory — OOMKilled, а упор в limits.cpu — троттлинг: процесс работает медленнее",
        "Под без requests и limits получает QoS BestEffort и вытесняется первым при нехватке памяти",
        "Если limits не указаны, Kubernetes сам ставит limit равным request, и контейнер не выйдет за него",
        "Сумма limits подов на ноде может превышать её память: overcommit по limits разрешён",
    ],
    correct_answer=[0, 2, 3, 5],
)

task(
    "do-k8s-basics-04", T, "find_bug", 4, "devops_colleague",
    "Дима: «Вчера база Маркета на 40 секунд ушла в failover. База вернулась, а каталог лежал ещё пять минут: все его поды перезапустились разом и долго прогревали кэш. Глянь манифест и код хелсчека — по-моему, мы сами себе выстрелили в ногу».",
    "Что в конфигурации проб привело к каскадному перезапуску и как правильно исправить?",
    "livenessProbe отвечает на вопрос «жив ли процесс», и при провале kubelet перезапускает контейнер. Здесь liveness ходит в /health, который проверяет базу и Redis: база ушла в failover — все поды одновременно провалили пробу 2 раза по 5 секунд и были перезапущены, хотя сами процессы были в порядке. Рестарт не лечит базу, зато сбрасывает кэш и превращает 40 секунд сбоя в пять минут. Правильно — лёгкий /healthz для liveness, который проверяет только сам процесс (event loop отвечает), а зависимости оставить максимум в readiness. Но и readiness, привязанная к базе, выведет из балансировки все поды сразу, поэтому многие команды проверяют в ней только критичное или отдают деградированный ответ. Увеличение таймаутов и числа реплик лишь отодвигает ту же проблему.",
    [
        "Что делает kubelet, когда не проходит liveness, а что — когда readiness?",
        "Поможет ли перезапуск пода, если недоступна база?",
    ],
    code="""# deployment.yaml (фрагмент контейнера catalog)
          readinessProbe:
            httpGet:
              path: /health
              port: 8000
            periodSeconds: 5
          livenessProbe:
            httpGet:
              path: /health
              port: 8000
            periodSeconds: 5
            timeoutSeconds: 1
            failureThreshold: 2

# app/health.py
# @app.get("/health")
# async def health(db: AsyncSession = Depends(get_db)):
#     await db.execute(text("SELECT 1"))   # проверяем базу
#     await redis.ping()                    # и кэш
#     return {"status": "ok"}
""",
    language="yaml",
    options=[
        "timeoutSeconds: 1 мал: при failover база отвечает дольше секунды — поставить 30, и рестартов не будет",
        "liveness проверяет базу и Redis, и при их сбое kubelet перезапускает живые поды. Для liveness — лёгкий /healthz без зависимостей",
        "readinessProbe дублирует liveness и удваивает нагрузку на базу во время сбоя — убрать readiness, оставить liveness",
        "Поставить replicas: 10 и maxUnavailable: 50% — часть подов переживёт перезапуск и удержит тёплый кэш",
        "failureThreshold: 2 слишком велик — при 1 сломанные поды перезапускаются и прогревают кэш быстрее",
    ],
    correct_answer=1,
)

task(
    "do-k8s-basics-05", T, "code_review", 4, "teamlead",
    "Марина: «Джун вынес настройки поиска в ConfigMap и пишет в описании PR: „теперь TTL кэша можно менять без релиза — поправил ConfigMap, и всё“. Посмотри манифесты перед мерджем и отметь замечания, которые реально надо оставить».",
    "Выбери все замечания, которые действительно стоит написать в ревью.",
    "Значения в data у ConfigMap — строки, и CACHE_TTL: 300 без кавычек YAML прочитает как число: kubectl apply отклонит объект с ошибкой cannot unmarshal number into Go struct field ConfigMap.data of type string. Переменные из envFrom читаются только при старте контейнера, поэтому правка ConfigMap не дойдёт до работающих подов — нужен kubectl rollout restart или checksum-аннотация в шаблоне, и обещание «без релиза» в описании вводит команду в заблуждение. Тег latest делает версию непредсказуемой, а rollout undo вернёт тот же latest. envFrom с configMapRef — штатный способ, Guaranteed-поды вытесняются последними, а не первыми, число реплик без метрик нагрузки не обсуждают (и PDB с двумя репликами вполне настраивается), а имя ConfigMap может быть длиной до 253 символов — это вкусовщина.",
    [
        "Какого типа должны быть значения в data у ConfigMap?",
        "Когда контейнер читает переменные окружения?",
        "Что вернёт rollout undo, если в прошлой ревизии тоже был latest?",
    ],
    code="""apiVersion: v1
kind: ConfigMap
metadata:
  name: search-config
data:
  CACHE_TTL: 300
  SEARCH_BACKEND: opensearch
  LOG_LEVEL: info
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: search
spec:
  replicas: 2
  selector:
    matchLabels:
      app: search
  template:
    metadata:
      labels:
        app: search
    spec:
      containers:
        - name: search
          image: registry.kodzilla.ru/market/search:latest
          envFrom:
            - configMapRef:
                name: search-config
          resources:
            requests:
              cpu: 200m
              memory: 256Mi
            limits:
              cpu: 200m
              memory: 256Mi
""",
    language="yaml",
    options=[
        "CACHE_TTL: 300 — число, а значения data в ConfigMap — строки: apply упадёт, нужно \"300\"",
        "envFrom читается при старте контейнера: правка ConfigMap не дойдёт до подов без rollout restart — «без релиза» неверно",
        "Тег latest: непонятно, какая версия крутится, а rollout undo вернёт тот же latest",
        "envFrom поддерживает только Secret — ConfigMap надо подключать через env и configMapKeyRef по ключу",
        "limits равны requests — под получит QoS Guaranteed, а такие поды вытесняются с ноды первыми",
        "Имя search-config стоит сократить до search-cfg: имена ConfigMap ограничены 16 символами",
        "replicas: 2 мало: без трёх реплик PodDisruptionBudget не даст спокойно обновлять ноды",
    ],
    correct_answer=[0, 1, 2],
)

# =====================================================================
# do-k8s-ingress-helm
# =====================================================================
TOPICS.append({"topic_id": "do-k8s-ingress-helm", "theory": """Service типа ClusterIP виден только внутри кластера. Чтобы пустить пользователей снаружи по домену, нужен Ingress — правила HTTP-маршрутизации: какой хост и путь в какой Service отправить. Сам объект Ingress ничего не делает, правила исполняет Ingress-контроллер (NGINX, Traefik, HAProxy, облачный балансировщик), его выбирают полем ingressClassName. Контекст 2026 года: community-контроллер ingress-nginx в марте 2026 снят с поддержки, новые кластеры берут другие контроллеры или Gateway API (там вместо Ingress — HTTPRoute). Сам ресурс Ingress стабилен, идеи те же.

Правило — это host и список путей. pathType: Prefix совпадает по сегментам (/api ловит /api и /api/orders, но не /apix), Exact — только точный путь. Цепочка запроса: Ingress → Service (name и port) → targetPort → порт контейнера. Видишь 502 или 503 — проверяй каждое звено: kubectl describe ingress, endpoints у Service, kubectl port-forward svc/catalog 8080:80.

TLS: в секции tls перечисляешь hosts и secretName — Secret типа kubernetes.io/tls с ключами tls.crt и tls.key. Руками сертификаты не выпускают: cert-manager по аннотации cert-manager.io/cluster-issuer сам получит сертификат Let's Encrypt, положит его в этот Secret и заранее продлит. Хост в tls должен совпадать с host в rules. TLS заканчивается на контроллере, дальше внутри кластера идёт обычный HTTP.

Helm — пакетный менеджер Kubernetes. Чарт — это Chart.yaml, папка templates/ с шаблонами манифестов (Go templates) и values.yaml со значениями по умолчанию. Релиз — установленный экземпляр чарта; каждый upgrade создаёт новую ревизию, история хранится в Secret'ах в namespace релиза.

    helm lint ./chart
    helm template catalog ./chart -f values-prod.yaml
    helm upgrade --install catalog ./chart -n market \\
      -f values-prod.yaml --set image.tag=2.10 --wait --atomic
    helm history catalog -n market
    helm rollback catalog 41 -n market

Приоритет значений: values.yaml чарта < файлы -f (последний главнее) < --set. Отличия стейджа и прода держи в values-stage.yaml и values-prod.yaml, а не в копиях шаблонов. Помни про типы YAML: tag: 2.10 превратится в число 2.1 — версии и теги бери в кавычки.

--wait заставляет Helm дождаться готовности подов, иначе релиз «успешен», как только API принял манифесты. --atomic при неудаче сам откатывает релиз. Если CI убили посреди upgrade, релиз зависает в статусе pending-upgrade, и любой следующий деплой падает с «another operation (install/upgrade/rollback) is in progress» — смотри helm history и откатывайся на последнюю ревизию в статусе deployed. Откат возвращает манифесты, но не схему базы.

Чтобы поды перезапускались при изменении ConfigMap из чарта, добавь в шаблон пода аннотацию:

    checksum/config: {{ include (print $.Template.BasePath "/configmap.yaml") . | sha256sum }}"""})

T = "do-k8s-ingress-helm"

task(
    "do-k8s-ingress-helm-01", T, "write_code", 3, "manager",
    "Стас: «Маркетинг купил домен market.kodzilla.ru, завтра запускаем рекламу. Надо, чтобы Маркет открывался по нему и обязательно с замочком — без HTTPS браузер пугает покупателей. Фронт и API уже крутятся в кластере, осталось их „выставить наружу“».",
    "Допиши Ingress market: класс контроллера nginx; хост market.kodzilla.ru; TLS с сертификатом от cert-manager (ClusterIssuer letsencrypt-prod) в секрете market-tls; запросы с префиксом /api — в Service api на порт 80, всё остальное — в Service frontend на порт 80.",
    "ingressClassName говорит, какой контроллер исполняет правила; без него Ingress может остаться бесхозным. Аннотация cert-manager.io/cluster-issuer запускает cert-manager: он выпустит сертификат Let's Encrypt для хостов из секции tls, положит его в market-tls и продлит заранее. Хост в tls и в rules должен совпадать, иначе контроллер отдаст свой самоподписанный сертификат. pathType: Prefix работает по сегментам пути, поэтому /api заберёт /api/orders, а / поймает всё остальное — более длинный префикс всегда выигрывает, порядок путей в списке не важен. Порт в backend — это port Service, а не порт контейнера.",
    [
        "TLS описывается в spec.tls: список hosts и secretName.",
        "cert-manager включается аннотацией в metadata.annotations.",
        "Добавь host в правило и второй путь /api с pathType: Prefix и backend service api.",
    ],
    code="""apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: market
spec:
  rules:
    - http:
        paths:
          - path: /
            pathType: Prefix
            backend:
              service:
                name: frontend
                port:
                  number: 80
""",
    language="yaml",
    correct_answer="""apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: market
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt-prod
spec:
  ingressClassName: nginx
  tls:
    - hosts:
        - market.kodzilla.ru
      secretName: market-tls
  rules:
    - host: market.kodzilla.ru
      http:
        paths:
          - path: /api
            pathType: Prefix
            backend:
              service:
                name: api
                port:
                  number: 80
          - path: /
            pathType: Prefix
            backend:
              service:
                name: frontend
                port:
                  number: 80
""",
    test_cases=[
        rx("ingressClassName: nginx", r"^\s*ingressClassName:\s*['\"]?nginx['\"]?\s*$"),
        rx("Аннотация cert-manager с ClusterIssuer letsencrypt-prod", r"^\s*['\"]?cert-manager\.io/cluster-issuer['\"]?:\s*['\"]?letsencrypt-prod['\"]?\s*$"),
        rx("TLS-секция с секретом market-tls", blk("tls", r"secretName:\s*['\"]?market-tls['\"]?")),
        rx("TLS выписывается для market.kodzilla.ru", blk("tls", r"market\.kodzilla\.ru")),
        rx("Правило для хоста market.kodzilla.ru", r"^\s*(?:- )?host:\s*['\"]?market\.kodzilla\.ru['\"]?\s*$"),
        rx("/api уходит в Service api", r"path:\s*['\"]?/api/?['\"]?\s*\n(?:(?![ ]*- )[^\n]*\n)*?[ ]+name:\s*['\"]?api['\"]?\s*$"),
        rx("/ уходит в Service frontend", r"path:\s*['\"]?/['\"]?\s*\n(?:(?![ ]*- )[^\n]*\n)*?[ ]+name:\s*['\"]?frontend['\"]?\s*$"),
    ],
)

task(
    "do-k8s-ingress-helm-02", T, "find_bug", 3, "qa",
    "Ира: «После переезда КофеБота в кластер его админка отдаёт 502 Bad Gateway. Шаги: открыть https://bot.kodzilla.ru/admin → 502. При этом оба пода Running и READY 1/1, в логах бота ни одного запроса».",
    "Почему Ingress отдаёт 502? Выбери диагноз и исправление.",
    "Запрос идёт по цепочке Ingress → Service port 80 → targetPort → порт в контейнере. Приложение слушает 8000, а Service отправляет трафик на 8080, где никого нет, — контроллер получает Connection refused и возвращает 502; лог контроллера это прямо показывает. Поды при этом Ready, потому что readinessProbe kubelet шлёт напрямую на 8000, минуя Service. Лечится targetPort: 8000, а надёжнее — дать порту контейнера имя (name: http) и писать targetPort: http, тогда номер порта живёт в одном месте. Ingress и должен ссылаться на порт Service, а не контейнера; Prefix / покрывает /admin, а между двумя подами трафик штатно балансирует Service.",
    [
        "Посмотри на адрес upstream в логе контроллера: какой там порт?",
        "Сравни targetPort у Service с портом, который реально слушает uvicorn.",
    ],
    code="""apiVersion: apps/v1
kind: Deployment
metadata:
  name: coffeebot
spec:
  replicas: 2
  selector:
    matchLabels:
      app: coffeebot
  template:
    metadata:
      labels:
        app: coffeebot
    spec:
      containers:
        - name: bot
          image: registry.kodzilla.ru/coffeebot:3.1.0
          args: ["uvicorn", "bot.web:app", "--host", "0.0.0.0", "--port", "8000"]
          ports:
            - containerPort: 8000
          readinessProbe:
            httpGet:
              path: /ready
              port: 8000
---
apiVersion: v1
kind: Service
metadata:
  name: coffeebot
spec:
  selector:
    app: coffeebot
  ports:
    - name: http
      port: 80
      targetPort: 8080
---
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: coffeebot
spec:
  ingressClassName: nginx
  tls:
    - hosts:
        - bot.kodzilla.ru
      secretName: coffeebot-tls
  rules:
    - host: bot.kodzilla.ru
      http:
        paths:
          - path: /
            pathType: Prefix
            backend:
              service:
                name: coffeebot
                port:
                  number: 80
# лог ingress-контроллера:
# connect() failed (111: Connection refused) while connecting to upstream,
#   host: "bot.kodzilla.ru", upstream: "http://10.42.3.17:8080/admin"
""",
    language="yaml",
    options=[
        "Ingress должен ссылаться на порт контейнера 8000, а не на port Service 80 — контроллер ходит прямо в под",
        "targetPort Service (8080) не совпадает с портом приложения (8000) — исправить targetPort на 8000 или имя порта",
        "pathType: Prefix с путём / не покрывает /admin — нужен отдельный путь /admin с pathType: Exact",
        "readinessProbe проверяет /ready, а контроллер ходит на /admin: для этого пути поды считаются неготовыми",
        "Ingress не балансирует между двумя подами без аннотации sticky sessions — поставить replicas: 1",
    ],
    correct_answer=1,
)

task(
    "do-k8s-ingress-helm-03", T, "write_code", 4, "teamlead",
    "Гена: «Каталог на проде до сих пор катят чартом с дефолтными values: одна реплика, тег latest, без ресурсов, Ingress включают руками через --set. На распродаже это нас положит. Сделай нормальный values для прода — CI будет передавать его через -f».",
    "Заполни values-prod.yaml (какие ключи читают шаблоны — в комментарии): 3 реплики; образ registry.kodzilla.ru/market/catalog с тегом 2.10; requests cpu 250m и memory 512Mi, limit памяти 1Gi; Ingress включён для хоста market.kodzilla.ru с TLS-секретом market-tls.",
    "values-prod.yaml переопределяет дефолты чарта, и в CI остаётся одна команда helm upgrade --install -f values-prod.yaml. Главная ловушка — тег: без кавычек YAML прочитает 2.10 как число 2.1, шаблон подставит catalog:2.1, и под уйдёт в ImagePullBackOff, а то и запустит старую версию. Поэтому версии и теги всегда в кавычках. resources шаблон вставляет через toYaml целиком, поэтому структура должна быть как в манифесте контейнера: requests и limits. Три реплики переживут падение ноды и rolling update без просадки, а Ingress со своим хостом и TLS в values означает, что прод больше не зависит от того, кто что передал в --set.",
    [
        "Посмотри в комментарии, какие ключи читают шаблоны, и заполни каждый.",
        "resources — это обычный блок requests/limits, как в манифесте контейнера.",
        "Что YAML сделает со значением 2.10 без кавычек?",
    ],
    code="""# Шаблоны чарта читают:
#   .Values.replicaCount
#   .Values.image.repository, .Values.image.tag
#   .Values.resources        (целиком вставляется в контейнер через toYaml)
#   .Values.ingress.enabled, .Values.ingress.host, .Values.ingress.tlsSecret
replicaCount: 1
image:
  repository: registry.kodzilla.ru/market/catalog
  tag: latest
resources: {}
ingress:
  enabled: false
""",
    language="yaml",
    correct_answer="""# Шаблоны чарта читают:
#   .Values.replicaCount
#   .Values.image.repository, .Values.image.tag
#   .Values.resources        (целиком вставляется в контейнер через toYaml)
#   .Values.ingress.enabled, .Values.ingress.host, .Values.ingress.tlsSecret
replicaCount: 3
image:
  repository: registry.kodzilla.ru/market/catalog
  tag: "2.10"
resources:
  requests:
    cpu: 250m
    memory: 512Mi
  limits:
    memory: 1Gi
ingress:
  enabled: true
  host: market.kodzilla.ru
  tlsSecret: market-tls
""",
    test_cases=[
        rx("3 реплики", r"^replicaCount:\s*3\s*$"),
        rx("Тег 2.10 в кавычках (иначе YAML прочитает 2.1)", r"^\s+tag:\s*(['\"])2\.10\1\s*$"),
        nrx("Нет тега latest", r"tag:\s*['\"]?latest"),
        rx("requests: cpu 250m", blk("requests", r"cpu:\s*['\"]?250m\b")),
        rx("requests: memory 512Mi", blk("requests", r"memory:\s*['\"]?512Mi\b")),
        rx("limits: memory 1Gi", blk("limits", r"memory:\s*['\"]?1Gi\b")),
        rx("Ingress включён", blk("ingress", r"enabled:\s*true\b")),
        rx("Хост market.kodzilla.ru и секрет market-tls", r"(?=[\s\S]*^\s*host:\s*['\"]?market\.kodzilla\.ru['\"]?\s*$)[\s\S]*^\s*tlsSecret:\s*['\"]?market-tls['\"]?\s*$"),
    ],
)

task(
    "do-k8s-ingress-helm-04", T, "incident", 4, "devops_colleague",
    "Дима: «CI упал посреди helm upgrade каталога — раннер убили по таймауту. Теперь любой деплой каталога падает с ошибкой, а через час надо выкатывать хотфикс по оплате. Я на созвоне с провайдером, разрули сам».",
    "Что сделать, чтобы разблокировать деплой и вернуть релиз в рабочее состояние?",
    "helm history показывает ревизию 42 в статусе pending-upgrade: Helm записал начало операции, а завершить её не успел, потому что раннер убили. Пока последняя ревизия в pending-статусе, любой upgrade отказывается работать. helm rollback на последнюю ревизию в статусе deployed (41) создаст ревизию 43 с манифестами 41: Deployment вернётся на 2.10, полувыкатанный под 2.11 в CrashLoopBackOff уберётся, а релиз снова станет deployed. Сам Helm блокировку не снимает, --force не обходит pending-статус, а uninstall удалит вместе с релизом Service и Ingress — это полноценный даунтайм. Потом разбираемся, почему 2.11 падает, а в CI добавляем --atomic и --timeout, чтобы Helm сам откатывался, а не умирал на полпути.",
    [
        "Посмотри на статус последней ревизии в helm history.",
        "Какая ревизия была последней рабочей (deployed)?",
    ],
    code="""$ helm upgrade --install catalog ./chart -n market -f values-prod.yaml --set image.tag=2.11.1 --wait
Error: UPGRADE FAILED: another operation (install/upgrade/rollback) is in progress

$ helm history catalog -n market
REVISION  UPDATED                   STATUS           CHART           APP VERSION  DESCRIPTION
40        Mon Sep 21 11:02:13 2026  superseded       catalog-0.9.0   2.9          Upgrade complete
41        Tue Sep 22 16:40:55 2026  deployed         catalog-0.9.1   2.10         Upgrade complete
42        Thu Sep 24 10:17:31 2026  pending-upgrade  catalog-0.9.2   2.11         Preparing upgrade

$ kubectl get pods -n market -l app=catalog
NAME                       READY   STATUS             RESTARTS      AGE
catalog-6d8f7b9c4-4kq2m    1/1     Running            0             2d
catalog-6d8f7b9c4-9wz7r    1/1     Running            0             2d
catalog-6d8f7b9c4-t5hbn    1/1     Running            0             2d
catalog-7b5c6d8f9-h2xpl    0/1     CrashLoopBackOff   6 (40s ago)   9m
""",
    language="text",
    options=[
        "helm uninstall catalog и поставить релиз заново: история очистится, и блокировка снимется",
        "helm rollback catalog 41 -n market: вернуть последнюю рабочую ревизию и снять pending-статус",
        "Подождать: Helm сам снимает зависшую операцию, когда истекает --timeout прошлого upgrade",
        "Повторить upgrade с --force: он пересоздаёт ресурсы и игнорирует незавершённые операции",
        "Удалить Deployment catalog через kubectl — Helm пересоздаст его при следующем upgrade",
        "Удалить под catalog-7b5c6d8f9-h2xpl в CrashLoopBackOff — блокировку держит именно он",
    ],
    correct_answer=1,
    time_limit=15,
)

task(
    "do-k8s-ingress-helm-05", T, "estimation", 4, "manager",
    "Стас: «Дима говорит, наши манифесты надо перевести на Helm, чтобы нормально жить со стейджем и продом, а то опять кто-то забыл поправить реплики в копии. Сколько это займёт? Мне надо вписать в план квартала, цифру дай сегодня».",
    "Прежде чем назвать срок, выбери вопросы и риски, которые нужно выяснить в первую очередь.",
    "Объём работы задают число сервисов и то, насколько похожи их манифесты: шесть однотипных API укладываются в один общий чарт с разными values, а зоопарк потребует нескольких чартов. Разъехавшиеся стейдж и прод и ручные правки прямо в кластере — главный скрытый риск: сначала их надо найти (kubectl diff против git), иначе первый же helm upgrade откатит чью-то ручную правку. Пайплайны CI придётся переделать на helm upgrade --install, это отдельная работа. Самое тонкое — переход без даунтайма: существующие объекты можно усыновить в релиз, проставив им лейбл app.kubernetes.io/managed-by: Helm и аннотации meta.helm.sh/release-name и release-namespace, иначе Helm откажется ставить релиз поверх них. Разумная оценка для шести похожих сервисов: общий чарт 2–3 дня, values и проверка на стейдже по полдня на сервис, CI 1–2 дня, переключение прода по одному сервису — итого около двух недель.",
    [
        "Что сильнее всего влияет на объём: число сервисов, их сходство или что-то ещё?",
        "Как перевести на Helm то, что уже работает в проде, без даунтайма?",
        "Кто и как сейчас деплоит — это тоже придётся менять.",
    ],
    options=[
        "Сколько сервисов и насколько похожи их манифесты — хватит ли одного общего чарта?",
        "Не разъехались ли стейдж, прод и то, что крутится в кластере, с тем, что лежит в git?",
        "Как CI деплоит сейчас и кто будет переделывать пайплайны на helm upgrade?",
        "Как взять под Helm уже работающие ресурсы без даунтайма — усыновить или пересоздать?",
        "Какую иконку и описание поставить чарту в Artifact Hub, чтобы его было легко найти?",
        "Можно ли заодно переписать сервисы на Go, раз уж всё равно трогаем их деплой?",
        "Уложимся ли в один вечер, если никто не будет отвлекать и все ревью пройдут сразу?",
    ],
    correct_answer=[0, 1, 2, 3],
    time_limit=15,
)

# =====================================================================
# do-k8s-debug
# =====================================================================
TOPICS.append({"topic_id": "do-k8s-debug", "theory": """Дебаг пода — всегда одна и та же лестница: get → describe → logs → events → exec. Не перескакивай ступени и не удаляй поды «на удачу»: новый под не лечит причину, зато стирает улики — лог упавшего контейнера.

    kubectl get pods -n market -o wide
    kubectl describe pod catalog-7c9d8f6b5-x2lqp -n market
    kubectl logs catalog-7c9d8f6b5-x2lqp -n market --previous
    kubectl get events -n market --sort-by=.lastTimestamp
    kubectl exec -it catalog-7c9d8f6b5-x2lqp -n market -- sh
    kubectl debug -it catalog-7c9d8f6b5-x2lqp -n market --image=busybox --target=catalog
    kubectl top pod -n market

Что значат статусы:
• Pending — под не назначен на ноду. В Events будет FailedScheduling: Insufficient cpu/memory (requests не влезают ни в одну ноду), untolerated taint, не подходит nodeSelector или affinity, не найден PVC. Логов нет: контейнер ещё не запускался.
• ContainerCreating — ждём образ, том, Secret или ConfigMap; причина в Events.
• ErrImagePull / ImagePullBackOff — образ не скачивается. not found или manifest unknown — опечатка в имени или теге; 401/403 — нет доступа, нужен imagePullSecrets. Secret'ы живут внутри namespace и между ними не видны.
• CrashLoopBackOff — контейнер стартует и падает, kubelet перезапускает его с растущей паузой (10 с, 20 с, 40 с… до 5 минут). Обычный kubectl logs покажет новый экземпляр, который ещё ничего не успел написать; причина — в kubectl logs --previous и в Last State (Reason, Exit Code).
• Running, но READY 0/1 — не проходит readinessProbe, трафик на под не идёт. В Events: Readiness probe failed. Частая причина — приложение слушает 127.0.0.1: изнутри контейнера всё отвечает, а kubelet и Service ходят на IP пода. В контейнере слушай 0.0.0.0.
• OOMKilled — превышен limits.memory.

Коды выхода: 0 — процесс завершился сам (для Deployment это тоже рестарт), 1 и другие небольшие — ошибка приложения, 137 = 128 + 9 (SIGKILL: OOM или добивание после grace period), 143 = 128 + 15 (SIGTERM).

Events хранятся недолго (по умолчанию час) — копируй вывод в тикет инцидента. Для distroless-образов без shell есть kubectl debug: он добавляет в под временный контейнер с инструментами.

Приоритет дежурного: сначала смягчить (kubectl rollout undo, вернуть рабочую версию), потом искать причину — и обязательно сообщить статус тем, кто ждёт. Посмотри, обслуживают ли прод старые поды: при maxUnavailable: 0 неудачный rollout просто застревает, а старая версия продолжает работать. Если rollout не уложился в progressDeadlineSeconds (по умолчанию 600 секунд), Deployment получает условие Progressing=False, но сам не откатывается — это решение за тобой."""})

T = "do-k8s-debug"

task(
    "do-k8s-debug-01", T, "quiz", 3, "teamlead",
    "Гена: «Со следующей недели ты в ротации дежурств вместе с Димой. Ночью думать некогда — статусы подов надо читать с первого взгляда. Давай проверим базу перед первым дежурством».",
    "Выбери все верные утверждения.",
    "В CrashLoopBackOff контейнер уже перезапущен, и обычный kubectl logs показывает новый экземпляр, который ещё ничего не написал, — лог упавшего даёт только --previous. Pending с FailedScheduling означает, что планировщик не нашёл ноду, контейнер не запускался и логов нет, поэтому смотрят describe и events. Running с READY 0/1 — процесс работает, но не проходит readinessProbe, и Service не шлёт на него трафик. Код 137 — это SIGKILL: его даёт и OOM, и добивание контейнера после grace period, а точную причину показывает Reason. ImagePullBackOff — проблема скачивания образа, память тут ни при чём, а удаление пода в CrashLoopBackOff просто создаст новый с той же ошибкой.",
    [
        "Какой экземпляр контейнера показывает kubectl logs, когда под уже перезапущен?",
        "137 = 128 + 9. Кто, кроме OOM killer, может послать сигнал 9?",
    ],
    options=[
        "В CrashLoopBackOff обычный kubectl logs показывает новый экземпляр, а причину падения — kubectl logs --previous",
        "Pending с FailedScheduling: планировщик не нашёл ноду, логов ещё нет — смотреть describe и events",
        "Exit Code 137 всегда означает, что контейнеру не хватило памяти и его убил OOM killer",
        "ImagePullBackOff часто лечится увеличением limits.memory: образ распаковывается в память контейнера",
        "Running с READY 0/1: контейнер работает, но не проходит readinessProbe и не получает трафик",
        "Под в CrashLoopBackOff лучше удалить: новый под сбросит счётчик рестартов и поднимется без backoff-паузы",
    ],
    correct_answer=[0, 1, 4],
)

task(
    "do-k8s-debug-02", T, "incident", 3, "devops_colleague",
    "Дима: «Поднял для клиента „Жми“ отдельный namespace zhmi-stage и задеплоил туда запись на тренировки тем же манифестом, что и в нашем stage. У нас работает, у них поды не стартуют. Артур ждёт демо через полчаса, глянь, пока я настраиваю ему домен».",
    "Что мешает поду стартовать и как это исправить?",
    "ImagePullBackOff говорит, что kubelet не может скачать образ, а ключевое — 401 Unauthorized от registry: образ есть, но kubelet пришёл за ним без учётных данных. В namespace stage лежит секрет registry-kodzilla типа kubernetes.io/dockerconfigjson, а в zhmi-stage секретов нет вообще, Secret'ы не видны между namespace'ами. Нужно создать там такой же секрет (kubectl create secret docker-registry … -n zhmi-stage) и сослаться на него в imagePullSecrets пода или добавить его в ServiceAccount default этого namespace. Опечатка в теге выглядела бы как not found или manifest unknown, раз registry ответил 401 — сеть до него есть, а imagePullPolicy: Never сломает запуск на любой ноде, где образа ещё нет.",
    [
        "Какой HTTP-код вернул registry?",
        "Сравни список секретов в двух namespace'ах.",
    ],
    code="""$ kubectl get pods -n zhmi-stage
NAME                      READY   STATUS             RESTARTS   AGE
booking-5f7d9c8b6-2mvkx   0/1     ImagePullBackOff   0          6m

$ kubectl describe pod booking-5f7d9c8b6-2mvkx -n zhmi-stage | sed -n '/Events:/,$p'
Events:
  Type     Reason     Age                 From               Message
  ----     ------     ----                ----               -------
  Normal   Scheduled  6m                  default-scheduler  Successfully assigned zhmi-stage/booking-5f7d9c8b6-2mvkx to node-2
  Normal   Pulling    4m (x4 over 6m)     kubelet            Pulling image "registry.kodzilla.ru/clients/zhmi-booking:1.4.0"
  Warning  Failed     4m (x4 over 6m)     kubelet            Failed to pull image "registry.kodzilla.ru/clients/zhmi-booking:1.4.0": failed to authorize: failed to fetch oauth token: unexpected status from GET request to https://registry.kodzilla.ru/token?scope=repository%3Aclients%2Fzhmi-booking%3Apull&service=registry: 401 Unauthorized
  Warning  Failed     4m (x4 over 6m)     kubelet            Error: ErrImagePull
  Normal   BackOff    55s (x21 over 6m)   kubelet            Back-off pulling image "registry.kodzilla.ru/clients/zhmi-booking:1.4.0"
  Warning  Failed     55s (x21 over 6m)   kubelet            Error: ImagePullBackOff

$ kubectl get secrets -n stage | grep registry
registry-kodzilla   kubernetes.io/dockerconfigjson   1      210d

$ kubectl get secrets -n zhmi-stage
No resources found in zhmi-stage namespace.
""",
    language="text",
    options=[
        "Тега 1.4.0 нет в репозитории clients/zhmi-booking — пересобрать образ и запушить его заново",
        "401 от registry: в zhmi-stage нет pull-секрета — создать docker-registry секрет и указать в imagePullSecrets",
        "node-2 не может достучаться до registry по сети — привязать под к другой ноде через nodeSelector",
        "Ноде не хватает памяти, чтобы распаковать образ, — поднять поду limits.memory и повторить деплой",
        "Поставить imagePullPolicy: Never — kubelet возьмёт образ из кэша ноды и не пойдёт в registry",
    ],
    correct_answer=1,
    time_limit=15,
)

task(
    "do-k8s-debug-03", T, "incident", 4, "manager",
    "Стас: «Вечером распродажа, а Дима пишет, что половина подов поиска двадцать минут висит в Pending — после вчерашнего PR „увеличил ресурсы поиску, чтобы не тормозил“. Два работающих пока держат нагрузку, но к вечеру нужны все четыре. Разберись, пожалуйста, что там, а я пока предупрежу маркетинг».",
    "В чём причина и какое исправление правильное?",
    "FailedScheduling с 3 Insufficient memory означает, что ни на одной рабочей ноде не осталось 6Gi незарезервированной памяти: на worker-1 из примерно 15Gi уже зарезервировано 9400Mi. Планировщик смотрит на requests, а не на реальное потребление, которое у поиска около 700Mi, — PR завысил запрос почти в девять раз. Правильно вернуть requests к реальному потреблению с запасом (например, 1Gi) и поставить limit около 1,5–2Gi, опираясь на метрики за неделю, а не на ощущения. Новые ноды решили бы симптом, но мы платили бы за память, которую никто не использует. Снимать taint с control-plane и удалять работающие поды — прямой путь к аварии, а limits на размещение вообще не влияют.",
    [
        "Что написано в FailedScheduling про memory?",
        "Сравни requests.memory с тем, что показывает kubectl top.",
        "Планировщик считает requests или реальное потребление?",
    ],
    code="""$ kubectl get pods -n market -l app=search
NAME                     READY   STATUS    RESTARTS   AGE
search-8c7d6f5b4-7kq2x   1/1     Running   0          22m
search-8c7d6f5b4-m9vlp   1/1     Running   0          22m
search-8c7d6f5b4-qz4tn   0/1     Pending   0          22m
search-8c7d6f5b4-x8wrd   0/1     Pending   0          22m

$ kubectl describe pod search-8c7d6f5b4-qz4tn -n market | sed -n '/Events:/,$p'
Events:
  Type     Reason            Age                From               Message
  ----     ------            ----               ----               -------
  Warning  FailedScheduling  2m (x9 over 22m)   default-scheduler  0/4 nodes are available: 1 node(s) had untolerated taint {node-role.kubernetes.io/control-plane: }, 3 Insufficient memory. preemption: 0/4 nodes are available: 1 Preemption is not helpful for scheduling, 3 No preemption victims found for incoming pod.

$ kubectl get deploy search -n market -o jsonpath='{.spec.template.spec.containers[0].resources}'
{"limits":{"memory":"6Gi"},"requests":{"cpu":"500m","memory":"6Gi"}}

$ kubectl describe node worker-1 | grep -A5 'Allocated resources'
Allocated resources:
  Resource           Requests      Limits
  --------           --------      ------
  cpu                2150m (53%)   4 (100%)
  memory             9400Mi (61%)  12Gi (80%)
# allocatable memory у каждой из трёх рабочих нод — около 15Gi

$ kubectl top pod -n market -l app=search
NAME                     CPU(cores)   MEMORY(bytes)
search-8c7d6f5b4-7kq2x   310m         712Mi
search-8c7d6f5b4-m9vlp   295m         688Mi
""",
    language="text",
    options=[
        "Нодам не хватает CPU: requests.cpu 500m на под — снизить до 100m, и поды поместятся",
        "Под просит 6Gi, а использует ~700Mi — ни на одной ноде нет 6Gi свободных requests. Вернуть requests ~1Gi, limit 1,5–2Gi",
        "Снять taint с control-plane ноды: там свободна память, и два пода поиска поместятся туда",
        "Удалить работающие поды поиска: освободятся их requests, и планировщик разместит все четыре",
        "Поднять limits.memory до 8Gi: планировщик резервирует место по limits, и под займёт ноду целиком",
        "Срочно добавить в кластер ноды по 32 ГБ: requests — решение команды поиска, и спорить с ним перед распродажей не время",
    ],
    correct_answer=1,
    time_limit=15,
)

task(
    "do-k8s-debug-04", T, "incident", 4, "qa",
    "Ира: «Новый сервис уведомлений для „Высоты“ задеплоили на stage, Вера ждёт письма о бронированиях переговорок. Поды Running, но письма не уходят, а gateway на /notify отвечает 503. Разработчик клянётся, что локально через uvicorn всё работало!»",
    "Почему поды не становятся Ready и как это исправить?",
    "Под Running, но READY 0/1, и в Events проба пишет connection refused на 10.42.2.31:8080 — на IP пода порт закрыт. Лог объясняет почему: uvicorn слушает 127.0.0.1:8080, то есть только loopback внутри контейнера. Изнутри запрос на localhost отвечает, а kubelet и Service приходят на IP пода и получают отказ. Нужно запускать с --host 0.0.0.0 (или задать хост через переменную окружения). Локально это не всплыло, потому что браузер и uvicorn были на одной машине. Задержка пробы не поможет — приложение уже стартовало, порт 8080 в логе uvicorn совпадает с пробой, а /ready изнутри отвечает правильно. Убрать readinessProbe значит пустить трафик на порт, где его всё равно отвергнут, а NetworkPolicy не объясняет, почему лог говорит 127.0.0.1.",
    [
        "Сравни адрес в ошибке пробы и адрес в логе uvicorn.",
        "Почему curl на localhost изнутри контейнера работает, а проба — нет?",
    ],
    code="""$ kubectl get pods -n vysota -l app=notifier
NAME                       READY   STATUS    RESTARTS   AGE
notifier-7f6b5d4c9-5hxkq   0/1     Running   0          12m
notifier-7f6b5d4c9-t8mzr   0/1     Running   0          12m

$ kubectl describe pod notifier-7f6b5d4c9-5hxkq -n vysota | sed -n '/Events:/,$p'
Events:
  Type     Reason     Age                 From     Message
  ----     ------     ----                ----     -------
  Warning  Unhealthy  2m (x70 over 12m)   kubelet  Readiness probe failed: Get "http://10.42.2.31:8080/ready": dial tcp 10.42.2.31:8080: connect: connection refused

$ kubectl logs notifier-7f6b5d4c9-5hxkq -n vysota
INFO:     Started server process [1]
INFO:     Waiting for application startup.
INFO:     Application startup complete.
INFO:     Uvicorn running on http://127.0.0.1:8080 (Press CTRL+C to quit)

$ kubectl exec notifier-7f6b5d4c9-5hxkq -n vysota -- curl -s localhost:8080/ready
{"status":"ready"}
""",
    language="text",
    options=[
        "Приложение не успевает стартовать до первой пробы — увеличить initialDelaySeconds до 60",
        "uvicorn слушает только 127.0.0.1, а kubelet и Service приходят на IP пода. Запускать с --host 0.0.0.0",
        "Эндпоинт /ready отвечает не тем JSON, и kubelet считает пробу проваленной — починить ответ в коде",
        "Убрать readinessProbe — поды сразу станут Ready, и трафик от gateway пойдёт на них",
        "NetworkPolicy в namespace vysota блокирует kubelet — разрешить трафик от нод к подам на 8080",
        "Проба и Service смотрят на 8080, а uvicorn по умолчанию слушает 8000 — поменять порт пробы",
    ],
    correct_answer=1,
    time_limit=15,
)

task(
    "do-k8s-debug-05", T, "incident", 5, "devops_colleague",
    "Дима в дежурном чате: «Вечерний релиз заказов (orders 3.2.0) раскатывается уже 15 минут и не доезжает. Алертов по 5xx нет, но релиз-менеджер спрашивает, что происходит и когда будет готово. Я на другом инциденте — возьми этот».",
    "Выбери все правильные действия дежурного.",
    "Прод жив: из-за maxUnavailable: 0 все четыре старых пода продолжают обслуживать запросы, а застрял только один новый под. Обычный kubectl logs пуст, потому что показывает только что перезапущенный экземпляр, а --previous даёт причину: версия 3.2.0 ждёт переменную PAYMENT_API_URL, а в ConfigMap ключ называется PAYMENT_URL. Правильный порядок: откатить rollout (kubectl rollout undo или helm rollback), чтобы Deployment вернулся в стабильное состояние, сообщить команде причину и договориться, где переименовать ключ, и дать релиз-менеджеру понятный статус. Удалять старые поды — значит своими руками устроить даунтайм, память тут ни при чём, увеличенный progressDeadlineSeconds только спрячет проблему, а удаление нового пода вернёт ту же ошибку.",
    [
        "Работает ли прод прямо сейчас? Посчитай Running-поды старой версии.",
        "Почему обычный kubectl logs пустой, а --previous — нет?",
        "Кроме техники, дежурный отвечает ещё и за коммуникацию.",
    ],
    code="""$ kubectl rollout status deploy/orders -n market
Waiting for deployment "orders" rollout to finish: 1 out of 4 new replicas have been updated...
error: deployment "orders" exceeded its progress deadline

$ kubectl get deploy orders -n market -o jsonpath='{.spec.strategy}'
{"rollingUpdate":{"maxSurge":1,"maxUnavailable":0},"type":"RollingUpdate"}

$ kubectl get pods -n market -l app=orders
NAME                      READY   STATUS             RESTARTS       AGE
orders-5c9f8d7b6-8tq4n    1/1     Running            0              3d
orders-5c9f8d7b6-fj2kw    1/1     Running            0              3d
orders-5c9f8d7b6-p7zxm    1/1     Running            0              3d
orders-5c9f8d7b6-w3rvd    1/1     Running            0              3d
orders-6b8d7c9f5-l4k8s    0/1     CrashLoopBackOff   7 (2m ago)     15m

$ kubectl logs orders-6b8d7c9f5-l4k8s -n market
$ kubectl logs orders-6b8d7c9f5-l4k8s -n market --previous
Traceback (most recent call last):
  File "/app/orders/settings.py", line 21, in <module>
    PAYMENT_API_URL = os.environ["PAYMENT_API_URL"]
                      ~~~~~~~~~~^^^^^^^^^^^^^^^^^^^
  File "<frozen os>", line 714, in __getitem__
KeyError: 'PAYMENT_API_URL'

$ kubectl get configmap orders-config -n market -o yaml | grep -i pay
  PAYMENT_URL: https://pay.internal.kodzilla.ru/api/v2
""",
    language="text",
    options=[
        "Откатить релиз: kubectl rollout undo deploy/orders -n market (или helm rollback)",
        "Передать команде причину из --previous: 3.2.0 ждёт PAYMENT_API_URL, а в ConfigMap — PAYMENT_URL",
        "Удалить старые поды orders-5c9f8d7b6-*, чтобы Deployment быстрее переключился на новую версию",
        "Увеличить limits.memory: CrashLoopBackOff с пустым логом почти всегда означает OOM",
        "Поставить progressDeadlineSeconds: 3600, чтобы rollout успел доехать и не падал по дедлайну",
        "Написать релиз-менеджеру: прод на старой версии, релиз откатываем, причина — нет переменной окружения",
        "Удалить новый под orders-6b8d7c9f5-l4k8s — ошибка могла быть разовой, новый под стартует чисто",
    ],
    correct_answer=[0, 1, 5],
    time_limit=20,
)

# =====================================================================
# do-terraform
# =====================================================================
TOPICS.append({"topic_id": "do-terraform", "theory": """Terraform описывает инфраструктуру кодом (HCL): сети, машины, базы, DNS. Ты пишешь, какой она должна быть, а Terraform сравнивает это с реальностью и показывает план изменений. Есть совместимый open-source форк OpenTofu (команда tofu) — синтаксис и приёмы те же.

Блоки:
• terraform { required_version, required_providers, backend } — версии и место хранения state.
• provider "aws" { region = var.region } — плагин для API облака. Ключи доступа в код не пишут: переменные окружения, профиль или OIDC-роль в CI.
• resource "aws_instance" "web" { … } — объект с адресом aws_instance.web. Ссылки вида aws_instance.web.public_ip становятся зависимостями, порядок создания Terraform выстроит сам.
• data — прочитать то, что создано не этим кодом.
• variable — входной параметр (type, default, description, validation, sensitive); значения — из terraform.tfvars, -var-file или TF_VAR_имя.
• output — что показать после apply и отдать другим модулям.
• module — переиспользуемый набор ресурсов. Версию фиксируй всегда: version = "~> 5.1" для registry или ?ref=v1.4.0 для git.

Цикл: terraform init → fmt и validate → plan -out=tfplan → apply tfplan. В CI plan показывают в MR и применяют именно сохранённый план. Читай plan целиком: + создать, ~ изменить, - удалить, -/+ пересоздать (ищи «forces replacement» — это может быть потеря данных).

State (terraform.tfstate) связывает ресурсы из кода с реальными ID и хранит секреты открытым текстом, поэтому в git ему не место. Команде нужен remote backend с шифрованием и версионированием, иначе у каждого свой state и два apply создадут дубли. В блоке backend переменные использовать нельзя.

    backend "s3" {
      bucket       = "kodzilla-tfstate"
      key          = "coffeebot/stage/terraform.tfstate"
      region       = "eu-central-1"
      encrypt      = true
      use_lockfile = true
    }

Блокировка: на время plan и apply Terraform берёт lock, второй запуск получит «Error acquiring the state lock». В S3-бэкенде блокирует lock-файл прямо в бакете (use_lockfile: стабильно с Terraform 1.11, тогда же блокировку через таблицу DynamoDB объявили устаревшей). Если CI-джобу убили посреди apply, lock остаётся: убедись по полям Who и Created, что никто не работает, и сними его через terraform force-unlock <ID>. -lock=false на общем state — путь к битому state.

Дрейф — расхождение реальности и кода: кто-то поправил ресурс в консоли. plan предложит вернуть всё как в коде, terraform plan -refresh-only покажет само расхождение. Лечение — перенести нужное изменение в код через MR или осознанно откатить. lifecycle { ignore_changes = [...] } — только для полей, которые законно меняет кто-то другой, например автоскейлер.

count создаёт копии по индексам: убрал элемент из середины списка — индексы сдвинулись, ресурсы пересоздаются. Для именованных наборов бери for_each. Переименовал ресурс — добавь блок moved, иначе Terraform удалит и создаст его заново; существующий ресурс забирают под управление блоком import."""})

T = "do-terraform"

task(
    "do-terraform-01", T, "write_code", 3, "teamlead",
    "Гена: «Для стейджа „Батона“ Дима скопировал прод-конфиг и руками поменял размер машины. Теперь у нас два почти одинаковых файла, которые уже успели разъехаться. Сделай так, чтобы окружения отличались только значениями переменных, а IP сервера было видно сразу после apply».",
    "Перепиши конфиг: переменная instance_type (type = string, по умолчанию \"t3.small\"); переменная environment (type = string, с description и без значения по умолчанию); используй их в ресурсе (тег Environment — значение environment); добавь output public_ip с публичным IP сервера.",
    "Переменные превращают копипасту двух окружений в один код с разными значениями: stage.tfvars и prod.tfvars или TF_VAR_environment в CI. У environment нет default сознательно: забыть указать окружение и случайно применить stage-настройки к проду хуже, чем получить ошибку, а validation дополнительно ловит опечатки. Ссылка var.instance_type вместо строки — единственное место, где задаётся размер. output public_ip выводит адрес после apply, его можно получить через terraform output public_ip в скриптах и передать другим модулям. Интерполяция \"baton-web-${var.environment}\" нужна только там, где значение встраивается в строку; для целого значения пиши просто var.environment.",
    [
        "Блок variable \"имя\" { type = …, default = … } объявляет переменную, использовать её — var.имя.",
        "Без default Terraform потребует значение при plan — это и нужно для environment.",
        "Output: output \"public_ip\" { value = aws_instance.web.public_ip }.",
    ],
    code="""resource "aws_instance" "web" {
  ami           = "ami-0a1b2c3d4e5f67890"
  instance_type = "t3.micro"

  tags = {
    Name        = "baton-web-stage"
    Environment = "stage"
  }
}
""",
    language="hcl",
    correct_answer="""variable "instance_type" {
  description = "Тип инстанса для веб-сервера"
  type        = string
  default     = "t3.small"
}

variable "environment" {
  description = "Окружение: stage или prod"
  type        = string

  validation {
    condition     = contains(["stage", "prod"], var.environment)
    error_message = "environment должен быть stage или prod."
  }
}

resource "aws_instance" "web" {
  ami           = "ami-0a1b2c3d4e5f67890"
  instance_type = var.instance_type

  tags = {
    Name        = "baton-web-${var.environment}"
    Environment = var.environment
  }
}

output "public_ip" {
  description = "Публичный IP веб-сервера"
  value       = aws_instance.web.public_ip
}
""",
    test_cases=[
        rx("Переменная instance_type: string, по умолчанию t3.small", r"variable\s+\"instance_type\"\s*\{(?=<B>\btype\s*=\s*string\b)(?=<B>\bdefault\s*=\s*\"t3\.small\")"),
        rx("Переменная environment с description и без default", r"variable\s+\"environment\"\s*\{(?=<B>\bdescription\s*=)(?=<B>\btype\s*=\s*string\b)(?!<B>\bdefault\s*=)"),
        rx("Ресурс берёт тип инстанса из переменной", r"^\s*instance_type\s*=\s*var\.instance_type\s*$"),
        nrx("Нет захардкоженного t3.micro", r"\"t3\.micro\""),
        rx("Тег Environment из переменной", r"^\s*Environment\s*=\s*(?:var\.environment|\"\$\{var\.environment\}\")\s*$"),
        rx("output public_ip с публичным IP инстанса", r"output\s+\"public_ip\"\s*\{<B>\bvalue\s*=\s*aws_instance\.web\.public_ip\b"),
    ],
)

task(
    "do-terraform-02", T, "write_code", 4, "devops_colleague",
    "Дима: «Вчера мы с Мариной одновременно сделали apply со своих ноутбуков. У каждого был свой локальный terraform.tfstate, и Terraform честно создал Маркету вторую базу. А ещё ключи от AWS лежат прямо в main.tf, который в git. Давай наводить порядок, пока не случилось что похуже».",
    "Перепиши конфиг: Terraform не ниже 1.11; провайдер hashicorp/aws с ограничением версии \"~> 6.0\"; state в S3-бакете kodzilla-tfstate по ключу market/prod/terraform.tfstate (регион eu-central-1) с шифрованием и нативной блокировкой через lock-файл; регион провайдера — из переменной region; никаких ключей доступа в коде.",
    "Общий S3-бэкенд даёт всей команде и CI один state, а use_lockfile не пустит второй apply, пока идёт первый: Terraform положит рядом со state lock-файл через условную запись, и второй запуск получит ошибку блокировки вместо второй базы. encrypt = true шифрует state в бакете — там лежат пароли и адреса. В блоке backend нельзя ссылаться на переменные, поэтому регион там записан строкой, а в провайдере — var.region. required_version и ограничение ~> 6.0 защищают от сюрпризов: старый Terraform не поймёт use_lockfile, а мажорное обновление провайдера может сломать ресурсы. Ключи из main.tf надо не только удалить, но и отозвать — они уже в истории git; провайдер сам возьмёт доступ из AWS_PROFILE, переменных окружения или OIDC-роли в CI.",
    [
        "backend \"s3\" живёт внутри блока terraform { }.",
        "Блокировка в S3 без DynamoDB включается одним параметром — use_lockfile.",
        "Из провайдера убери access_key и secret_key: SDK AWS найдёт учётные данные в окружении.",
    ],
    code="""terraform {
  required_providers {
    aws = {
      source = "hashicorp/aws"
    }
  }
}

provider "aws" {
  region     = "eu-central-1"
  access_key = "AKIAIOSFODNN7EXAMPLE"
  secret_key = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
}

variable "region" {
  type    = string
  default = "eu-central-1"
}
""",
    language="hcl",
    correct_answer="""terraform {
  required_version = ">= 1.11"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }

  backend "s3" {
    bucket       = "kodzilla-tfstate"
    key          = "market/prod/terraform.tfstate"
    region       = "eu-central-1"
    encrypt      = true
    use_lockfile = true
  }
}

# Учётные данные — из окружения: AWS_PROFILE, переменные AWS_* или OIDC-роль в CI
provider "aws" {
  region = var.region
}

variable "region" {
  type    = string
  default = "eu-central-1"
}
""",
    test_cases=[
        rx("Terraform не ниже 1.11", r"required_version\s*=\s*\"[^\"]*\b1\.(?:1[1-9]|[2-9]\d)"),
        rx("Провайдер aws ограничен версией ~> 6.0", r"version\s*=\s*\"~>\s*6\.\d+(?:\.\d+)?\""),
        rx("Backend s3: бакет kodzilla-tfstate и ключ market/prod/terraform.tfstate", r"backend\s+\"s3\"\s*\{(?=<B>\bbucket\s*=\s*\"kodzilla-tfstate\")(?=<B>\bkey\s*=\s*\"market/prod/terraform\.tfstate\")"),
        rx("State шифруется", r"backend\s+\"s3\"\s*\{<B>\bencrypt\s*=\s*true\b"),
        rx("Нативная блокировка через lock-файл", r"backend\s+\"s3\"\s*\{<B>\buse_lockfile\s*=\s*true\b"),
        rx("Регион провайдера из переменной", r"provider\s+\"aws\"\s*\{<B>\bregion\s*=\s*var\.region\b"),
        nrx("Никаких ключей доступа в коде", r"(access_key|secret_key)\s*=|AKIA[0-9A-Z]{12,}", "i"),
    ],
)

task(
    "do-terraform-03", T, "code_review", 4, "teamlead",
    "Марина: «Джун завёл бакеты под картинки товаров, чеки и выгрузки для аналитики, подключил модуль сети из нашего общего репозитория и добавил воркер для очередей. plan зелёный, но зелёный plan — не аргумент. Пройдись по PR и отметь, что реально надо исправить до мерджа».",
    "Выбери все замечания, которые действительно стоит оставить в ревью.",
    "count адресует бакеты по индексу: aws_s3_bucket.market[0], [1], [2]. Если через полгода убрать \"images\" из начала списка, receipts и analytics-export сдвинутся на другие индексы, и Terraform захочет пересоздать бакеты с данными — for_each = toset(var.buckets) даёт стабильные адреса по имени. Модуль из git без ?ref= на каждом чистом init в CI скачивает текущий main, и чужое изменение модуля молча уедет в прод; фиксируй тег. Захардкоженный subnet_id не связан с сетью, которую создаёт модуль рядом: Terraform не знает о зависимости, а в новом окружении такой подсети просто нет — ID надо брать из output модуля. Остальное — либо вкусовщина, либо неправда: HCL — нормальный формат, local-exec вместо ресурсов ломает идемпотентность, style guide Terraform как раз советует snake_case, а instance_type строкой — стандартная практика.",
    [
        "Что станет с адресами бакетов, если убрать первый элемент списка?",
        "Какую версию модуля скачает CI при следующем чистом init?",
        "Откуда Terraform узнает, что воркер надо создавать после сети?",
    ],
    code="""variable "buckets" {
  type    = list(string)
  default = ["images", "receipts", "analytics-export"]
}

resource "aws_s3_bucket" "market" {
  count  = length(var.buckets)
  bucket = "kodzilla-market-${var.buckets[count.index]}"
}

module "network" {
  source = "git::https://git.kodzilla.ru/infra/tf-modules.git//network"

  cidr = "10.20.0.0/16"
  env  = "prod"
}

resource "aws_instance" "worker" {
  ami           = "ami-0a1b2c3d4e5f67890"
  instance_type = "c6i.large"
  subnet_id     = "subnet-0f1e2d3c4b5a69788"

  tags = {
    Name = "market-queue-worker"
  }
}
""",
    language="hcl",
    options=[
        "count по списку: убрал элемент из начала — индексы сдвинутся, и бакеты с данными пересоздадутся. Нужен for_each",
        "Модуль из git без ?ref=: при чистом init в CI приедет текущий main. Зафиксировать тег, например ?ref=v1.4.0",
        "subnet_id захардкожен: нет зависимости от модуля сети, а в новом окружении такой подсети нет. Брать из output модуля",
        "Лучше перейти с HCL на JSON-синтаксис: его проще генерировать и проверять линтерами в CI",
        "Бакеты надёжнее создавать через provisioner \"local-exec\" с aws s3 mb: видно, что команда реально выполнилась",
        "Имена ресурсов market и worker надо писать в PascalCase — так требует style guide Terraform",
        "instance_type нельзя задавать строкой: тип берут из data \"aws_ec2_instance_type\", иначе plan его не проверит",
    ],
    correct_answer=[0, 1, 2],
)

task(
    "do-terraform-04", T, "incident", 4, "devops_colleague",
    "Дима: «Хотфикс для security group надо применить прямо сейчас, а terraform apply в CI падает на блокировке. Днём я отменял зависший пайплайн — может, связано? Я за рулём, действуй сам, только аккуратно — это прод».",
    "Выбери все правильные действия.",
    "Lock держит CI-раннер с операцией apply, созданной в 11:42 UTC, а пайплайн #18342 отменён в 11:43: раннер убили, и снять блокировку он не успел. Раз активных запусков для этого state нет, lock сиротский, и его снимают командой terraform force-unlock с ID из Lock Info. Но отменённый apply мог успеть изменить часть ресурсов, а старый tfplan построен на прежнем state, поэтому после разблокировки нужен свежий plan и внимательное чтение изменений. -lock=false открывает дорогу параллельной записи и битому state, удаление state заставит Terraform забыть о всей инфраструктуре и попытаться создать её заново, а сама по себе блокировка в S3 не истекает.",
    [
        "Кто держит блокировку и жив ли этот процесс?",
        "Какую команду Terraform предлагает для снятия блокировки по ID?",
        "Можно ли доверять плану, который построили до прерванного apply?",
    ],
    code="""$ terraform apply tfplan
╷
│ Error: Error acquiring the state lock
│
│ Error message: operation error S3: PutObject, https response error
│ StatusCode: 412, RequestID: 8Y2KQ1VZ7M3T0B4X, api error PreconditionFailed:
│ At least one of the pre-conditions you specified did not hold
│ Lock Info:
│   ID:        6f0f8b1e-3c2d-9a47-51b6-2e8d4c7a9f13
│   Path:      kodzilla-tfstate/market/prod/terraform.tfstate
│   Operation: OperationTypeApply
│   Who:       runner@gitlab-runner-7f9c4
│   Version:   1.14.3
│   Created:   2026-09-24 11:42:07.518204 +0000 UTC
│   Info:
│
│ Terraform acquires a state lock to protect the state from being written
│ by multiple users at the same time. Please resolve the issue above and try
│ again. For most commands, you can disable locking with the "-lock=false"
│ flag, but this is not recommended.
╵

$ date -u
Thu Sep 24 15:03:51 UTC 2026

# GitLab: пайплайн #18342 (terraform apply, prod) — canceled в 11:43 UTC.
# Других запущенных пайплайнов для этого state нет.
""",
    language="text",
    options=[
        "Убедиться, что lock никто не держит: Who — CI-раннер, пайплайн #18342 отменён, других apply нет",
        "Снять блокировку: terraform force-unlock 6f0f8b1e-3c2d-9a47-51b6-2e8d4c7a9f13",
        "Запустить apply с -lock=false: хотфикс срочный, а других запусков всё равно нет",
        "Удалить из бакета state вместе с lock-файлом — Terraform пересоздаст state при apply",
        "После разблокировки сделать свежий plan: отменённый apply мог что-то поменять, старый tfplan устарел",
        "Подождать: lock-файл в S3 сам истекает через несколько часов, если его не обновлять",
    ],
    correct_answer=[0, 1, 4],
    time_limit=15,
)

task(
    "do-terraform-05", T, "incident", 5, "manager",
    "Стас: «Срочно! Аналитики партнёра с десяти утра не могут выгрузить данные из нашей базы — у них таймауты подключения, а вчера всё работало. Дима говорит, утром CI что-то применял в Terraform, но там же была какая-то ерунда с тегами?»",
    "Что произошло и какое решение правильное?",
    "Это дрейф, который apply «починил» против нашей воли. Правило для 203.0.113.25/32 на 5432 кто-то добавил руками в консоли (судя по описанию — Дима, временно), в коде его не было. Утренний MR менял только тег, но apply приводит ресурс целиком к коду, поэтому снёс неизвестное ему правило — и plan это честно показывал. Правильно: подтвердить, что доступ партнёру всё ещё нужен, описать правило в коде через MR и применить; тогда следующий apply его не тронет. Правка в консоли вернёт доступ до ближайшего apply, ignore_changes навсегда спрячет любые изменения правил, включая наши, apply -refresh-only только обновит state по реальности, где правила уже нет, а откат state не меняет реальную инфраструктуру. Урок на будущее — менять прод только через код и читать plan целиком, а не только свою строчку.",
    [
        "Сравни, что меняли в MR, и что plan собирался сделать на самом деле.",
        "Откуда взялось правило, которого не было в коде?",
        "Какое решение переживёт следующий terraform apply?",
    ],
    code="""# Лог утреннего пайплайна (terraform apply, prod), 09:58
# MR !4127: "security group db: добавить тег Owner"

Terraform will perform the following actions:

  # aws_security_group.db will be updated in-place
  ~ resource "aws_security_group" "db" {
        id                     = "sg-0c4f5e6d7a8b9c0d1"
      ~ ingress                = [
          - {
              - cidr_blocks      = [
                  - "203.0.113.25/32",
                ]
              - description      = "partner analytics (temp, Dima)"
              - from_port        = 5432
              - protocol         = "tcp"
              - to_port          = 5432
                # (4 unchanged attributes hidden)
            },
            # (1 unchanged element hidden)
        ]
        name                   = "market-db"
      ~ tags                   = {
          + "Owner" = "platform"
            # (1 unchanged element hidden)
        }
        # (6 unchanged attributes hidden)
    }

Plan: 0 to add, 1 to change, 0 to destroy.
aws_security_group.db: Modifying... [id=sg-0c4f5e6d7a8b9c0d1]
aws_security_group.db: Modifications complete after 2s [id=sg-0c4f5e6d7a8b9c0d1]

Apply complete! Resources: 0 added, 1 changed, 0 destroyed.
""",
    language="text",
    options=[
        "Вернуть правило руками в консоли облака — так быстрее всего, партнёр снова подключится через минуту",
        "Подтвердить, что доступ партнёру нужен, описать правило для 203.0.113.25/32:5432 в коде через MR и применить",
        "Добавить в security group lifecycle { ignore_changes = [ingress] } — Terraform перестанет трогать ручные правила",
        "Выполнить terraform apply -refresh-only: он сверит state с облаком и вернёт удалённое правило",
        "Откатить версию state в бакете на вчерашнюю — Terraform увидит правило и восстановит его в облаке",
    ],
    correct_answer=1,
    time_limit=20,
)

# =====================================================================
# do-ansible
# =====================================================================
TOPICS.append({"topic_id": "do-ansible", "theory": """Ansible настраивает серверы по SSH без агентов: на управляемых машинах нужен только Python. Ты описываешь желаемое состояние в YAML, Ansible приводит к нему каждый сервер.

Инвентори — список хостов и групп (INI или YAML), переменные групп и хостов лежат в group_vars/ и host_vars/:

    [web]
    baton-web-1 ansible_host=10.0.1.11
    baton-web-2 ansible_host=10.0.1.12

Плейбук — список плеев: на какие hosts, с какими правами, какие tasks. Задача вызывает модуль: apt/dnf (пакеты), copy и template (файлы; template рендерит Jinja2), file, lineinfile, user, systemd_service (короткое имя systemd), uri, git. Пиши полные имена модулей (ansible.builtin.apt) — этого ждёт ansible-lint, и не будет конфликтов с коллекциями.

Идемпотентность — главное свойство: повторный прогон на настроенном сервере ничего не меняет (в итоге changed=0). Модуль сначала проверяет текущее состояние и действует, только если оно отличается. shell и command слепо выполняют команду каждый раз и всегда отчитываются changed — бери их, только если модуля нет, и тогда добавляй creates/removes или changed_when.

become: true — выполнять через sudo (become_user — от чьего имени). Ставится на плей или на задачу; писать sudo внутри shell не нужно.

Хендлеры — задачи, которые запускаются по notify и только если уведомившая задача что-то изменила. Выполняются один раз в конце секции задач плея, сколько бы задач их ни позвали. Имя в notify должно совпадать с именем хендлера. Если плей упал раньше, хендлеры не запустятся, а при следующем прогоне шаблон уже не изменится — сервис так и не перечитает конфиг. Спасают --force-handlers (force_handlers: true) и meta: flush_handlers.

    tasks:
      - name: Конфиг chrony
        ansible.builtin.template:
          src: chrony.conf.j2
          dest: /etc/chrony/chrony.conf
          mode: "0644"
        notify: Restart chrony
    handlers:
      - name: Restart chrony
        ansible.builtin.systemd_service:
          name: chrony
          state: restarted

lineinfile правит одну строку. Без regexp он только дописывает line в конец, если точно такой строки нет, — старое значение останется выше. С regexp заменяет найденную строку (если совпадений несколько — последнюю). Для критичных конфигов добавляй validate: validate: /usr/sbin/sshd -t -f %s не даст положить конфиг, с которым sshd не стартует.

Роли раскладывают код по папкам roles/<имя>/tasks, handlers, templates, defaults (значения по умолчанию, их легко переопределить). Для деплоя без даунтайма у плея есть serial (сколько хостов обновлять за раз) и max_fail_percentage, а delegate_to выполняет задачу на другом хосте — например, выводит сервер из балансировщика.

Перед прогоном на проде: ansible-playbook site.yml --check --diff --limit baton-web-1, а в CI — ansible-lint."""})

T = "do-ansible"

task(
    "do-ansible-01", T, "write_code", 3, "devops_colleague",
    "Дима: «Плейбук для серверов пекарни „Батон“ писали наспех — одни shell-команды. Каждый прогон перезапускает nginx посреди дня, и Нина уже звонила: заказы на секунду отваливаются. Сделай так, чтобы повторный прогон ничего не трогал, а nginx перечитывал конфиг, только когда тот реально поменялся».",
    "Перепиши плейбук на идемпотентные модули: become на уровне плея; nginx ставится модулем apt (state: present); конфиг раскладывается модулем template из baton.conf.j2 в /etc/nginx/conf.d/baton.conf и уведомляет хендлер «Reload nginx»; хендлер делает reload модулем systemd (или systemd_service); сервис nginx запущен и включён в автозагрузку. Никаких shell и command.",
    "apt с state: present проверяет, стоит ли пакет, и ничего не делает, если стоит, а cache_valid_time не дёргает apt update на каждом прогоне. template сравнивает отрендеренный файл с тем, что на сервере, и сообщает changed, только если содержимое отличается, — лишь тогда срабатывает notify. Хендлер делает reload, а не restart: nginx перечитывает конфиг без разрыва текущих соединений, и заказы перестают отваливаться. Задача systemd со state: started и enabled: true гарантирует, что сервис работает и переживёт перезагрузку, но не трогает уже запущенный. become на уровне плея заменяет sudo внутри команд, а второй прогон такого плейбука даёт changed=0 — это и есть проверка идемпотентности.",
    [
        "Каждую shell-команду замени модулем: apt, template, systemd.",
        "notify ставится на задачу template, а сам хендлер — в секции handlers на уровне плея.",
        "Перезапуск в tasks не нужен: достаточно state: started и enabled: true, а reload сделает хендлер.",
    ],
    code="""- name: Настроить веб-сервер Батона
  hosts: baton_web
  tasks:
    - name: Установить nginx
      shell: sudo apt-get update && sudo apt-get install -y nginx

    - name: Положить конфиг
      shell: sudo cp /tmp/baton.conf /etc/nginx/conf.d/baton.conf

    - name: Перезапустить nginx
      shell: sudo systemctl restart nginx
""",
    language="yaml",
    correct_answer="""- name: Настроить веб-сервер Батона
  hosts: baton_web
  become: true

  tasks:
    - name: Установить nginx
      ansible.builtin.apt:
        name: nginx
        state: present
        update_cache: true
        cache_valid_time: 3600

    - name: Положить конфиг сайта
      ansible.builtin.template:
        src: baton.conf.j2
        dest: /etc/nginx/conf.d/baton.conf
        owner: root
        group: root
        mode: "0644"
      notify: Reload nginx

    - name: nginx запущен и в автозагрузке
      ansible.builtin.systemd:
        name: nginx
        state: started
        enabled: true

  handlers:
    - name: Reload nginx
      ansible.builtin.systemd:
        name: nginx
        state: reloaded
""",
    test_cases=[
        rx("Повышение прав через become", r"^\s*become:\s*(?:true|yes)\s*$"),
        nrx("Нет shell, command и sudo в командах", r"^\s*(?:- )?(?:ansible\.builtin\.)?(?:shell|command|raw):|\bsudo\s"),
        rx("nginx ставится модулем apt со state: present", blk(r"(?:ansible\.builtin\.)?apt", r"state[:=]\s*['\"]?present\b")),
        rx("Конфиг раскладывается модулем template в /etc/nginx/conf.d/baton.conf", blk(r"(?:ansible\.builtin\.)?template", r"dest[:=]\s*['\"]?/etc/nginx/conf\.d/baton\.conf\b")),
        rx("Задача с конфигом уведомляет «Reload nginx»", r"^\s*notify:\s*(?:\[\s*|\n\s*-\s*)?['\"]?Reload nginx['\"]?"),
        rx("Есть хендлер «Reload nginx»", blk("handlers", r"name:\s*['\"]?Reload nginx['\"]?\s*$")),
        rx("Хендлер делает reload, а не restart", blk("handlers", r"state[:=]\s*['\"]?reloaded\b")),
        rx("nginx запущен и в автозагрузке", r"(?=[\s\S]*^\s*state[:=]\s*['\"]?started\b)[\s\S]*^\s*enabled[:=]\s*(?:true|yes)\b"),
    ],
)

task(
    "do-ansible-02", T, "find_bug", 4, "teamlead",
    "Марина: «Аудитор банка-партнёра нашёл, что на серверах Маркета до сих пор можно зайти по SSH с паролем. Как так? У нас же плейбук харденинга, и он в каждом прогоне зелёный. Разберись, почему он не работает, и не вздумай просто перезагружать серверы».",
    "Почему вход по паролю всё ещё разрешён? Выбери верный диагноз и исправление.",
    "lineinfile без regexp проверяет только, есть ли в файле точно такая строка, и если нет — дописывает её в конец. Старая строка PasswordAuthentication yes на 58-й строке осталась, а sshd для каждой директивы берёт первое найденное значение, поэтому пароли разрешены, хотя плейбук честно зелёный. Нужен regexp вида ^#?\\s*PasswordAuthentication, чтобы модуль заменял существующую строку. Нюанс: при нескольких совпадениях lineinfile меняет только последнее, поэтому уже появившийся дубль сначала убери (модулем replace или lineinfile со state: absent), а надёжнее вообще положить отдельный файл в /etc/ssh/sshd_config.d/ — он подключается через Include в начале конфига и выигрывает. Добавь validate: /usr/sbin/sshd -t -f %s и проверяй итог через sshd -T | grep -i passwordauthentication.",
    [
        "Посмотри на вывод grep: сколько строк PasswordAuthentication в файле и в каком порядке?",
        "Какое значение берёт sshd, если директива повторяется?",
        "Что делает lineinfile, когда regexp не задан?",
    ],
    code="""- name: Харденинг SSH
  hosts: market
  become: true
  tasks:
    - name: Запретить вход по паролю
      ansible.builtin.lineinfile:
        path: /etc/ssh/sshd_config
        line: PasswordAuthentication no
      notify: Restart ssh

  handlers:
    - name: Restart ssh
      ansible.builtin.systemd:
        name: ssh
        state: restarted

# $ ansible market -b -m shell -a "grep -n PasswordAuthentication /etc/ssh/sshd_config"
# market-app-1 | CHANGED | rc=0 >>
# 58:PasswordAuthentication yes
# 124:PasswordAuthentication no
""",
    language="yaml",
    options=[
        "Не хватает become: true на уровне задачи — без него lineinfile молча не меняет файлы в /etc",
        "lineinfile без regexp дописал строку в конец, а «PasswordAuthentication yes» выше осталась; sshd берёт первую — нужен regexp",
        "Хендлер не срабатывает: notify сравнивает имена с учётом регистра — переименовать хендлер в restart ssh",
        "sshd берёт последнее значение директивы, значит, конфиг верный — аудитор видит кэш своего SSH-клиента",
        "lineinfile не умеет редактировать существующие строки — заменить задачу на shell с sed -i",
        "На Ubuntu сервис называется sshd, а не ssh, поэтому хендлер ничего не перезапускает и новый конфиг так и не применяется",
    ],
    correct_answer=1,
)

task(
    "do-ansible-03", T, "estimation", 4, "manager",
    "Стас: «Бизнес-центр „Высота“ отдаёт нам на поддержку свои 25 серверов, которые их прошлый админ пять лет настраивал руками. Вера хочет, чтобы всё было „как код“, как у нас. Дима предлагает Ansible. Сколько это займёт? Цифра нужна для договора, так что без „ну примерно“».",
    "Прежде чем назвать срок, выбери вопросы и риски, которые нужно выяснить в первую очередь.",
    "Объём задают роли серверов и зоопарк ОС: 25 одинаковых веб-серверов на одной Ubuntu — это одна роль, а веб, почта, база и файловые шары на трёх версиях ОС — пять-шесть ролей с вариантами. Главный риск пяти лет ручной настройки — незадокументированные различия: без аудита плейбук «приведёт к стандарту» сервер, на котором держалось что-то важное. Стенд или пара некритичных серверов нужны, чтобы прогнать --check --diff и обкатать роли до прода. Доступы с sudo и окна работ определяют, сколько времени уйдёт на ожидание, а не на работу. Разумно предложить Вере два этапа: аудит и инвентаризация (около недели, фиксированная цена), затем роли по 2–4 дня каждая с постепенной раскаткой — для 5–6 ролей это ещё 4–6 недель.",
    [
        "От чего число плейбуков и ролей зависит сильнее всего?",
        "Что может сломаться, если привести к стандарту сервер, про который мы ничего не знаем?",
        "Где ты будешь проверять плейбуки, прежде чем идти на прод клиента?",
    ],
    options=[
        "Какие роли у серверов (веб, база, почта, шары) и сколько на них разных ОС и версий?",
        "Есть ли стенд или пара некритичных серверов, чтобы обкатать плейбуки до прода?",
        "Насколько отличаются серверы одной роли — есть ли ручные правки, о которых знал только прошлый админ?",
        "Будет ли SSH-доступ с sudo ко всем серверам и когда окна для работ с перезапусками?",
        "Писать плейбуки в YAML или в JSON — что будет проще читать самой Вере?",
        "Можно ли сразу перевезти все 25 серверов в Kubernetes, раз всё равно их трогаем?",
        "Сколько строк получится в самом длинном плейбуке и уложимся ли в лимит ansible-lint?",
    ],
    correct_answer=[0, 1, 2, 3],
    time_limit=15,
)

task(
    "do-ansible-04", T, "architecture", 4, "qa",
    "Ира: «Поймала закономерность: во время каждого деплоя КофеБота заказы минуту падают с 502 — на всех шести серверах за HAProxy разом. Шаги: запустить деплой плейбуком → в эту минуту оформить заказ → 502. А на прошлой неделе новая версия не стартовала, и мы лежали, пока Дима не откатил руками. Можно деплоить так, чтобы пользователи этого не замечали?»",
    "Какой подход к деплою плейбуком правильный?",
    "По умолчанию Ansible выполняет каждую задачу сразу на всех хостах плея, поэтому шаг «перезапустить сервис» кладёт все шесть серверов одновременно. serial: 1 или 2 превращает плей в последовательные партии: пока обновляется партия, остальные серверы обслуживают запросы. Вывод сервера из HAProxy через delegate_to на балансировщик перед обновлением и возврат после health-check (модуль uri с until и retries) убирают даже короткие ошибки, а max_fail_percentage: 0 останавливает деплой на первой же неудачной партии, пока большинство серверов ещё на старой версии. forks только ограничивает параллельность внутри задачи, но задачи по-прежнему идут шагами на всех хостах, strategy: free не даёт ни проверок, ни остановки, async с poll: 0 перестаёт ждать сервис и прячет ошибки, а ночной деплой — не решение, а перенос проблемы на ночь.",
    [
        "В каком порядке Ansible по умолчанию выполняет задачи на нескольких хостах?",
        "Как сделать, чтобы в каждый момент обновлялась только часть серверов?",
        "Что должно произойти, если новая версия не прошла проверку на первом сервере?",
    ],
    options=[
        "serial: 1–2; сервер выводить из HAProxy через delegate_to, обновлять, ждать health-check (uri + retries), возвращать; max_fail_percentage: 0",
        "forks: 1 в ansible.cfg: Ansible будет ходить по серверам строго по одному, и пока обновляется один, пять остальных обслуживают запросы",
        "strategy: free: каждый сервер проходит плейбук в своём темпе, перезапуски разъедутся по времени, и балансировщик найдёт живой бэкенд",
        "async и poll: 0 на задаче перезапуска: Ansible не ждёт сервис и сразу идёт дальше, поэтому перезапуски не совпадут",
        "Деплоить ночью по cron, когда в офисе никто не заказывает кофе, а откат при неудаче оставить дежурному",
    ],
    correct_answer=0,
)


def main():
    data = {"track": "devops", "part": "d3", "topics": TOPICS, "tasks": TASKS}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print("written", OUT, len(TASKS), "tasks")


if __name__ == "__main__":
    main()
