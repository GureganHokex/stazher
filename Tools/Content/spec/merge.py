# Сборка итоговых файлов направлений в формате задания: {track, roadmap[...+theory], tasks[...+title]}
import json, glob, collections, os
S='Tools/Content/'
road=json.load(open(S+'spec/roadmap.json',encoding='utf-8'))
titles=json.load(open(S+'spec/titles.json',encoding='utf-8'))
parts=collections.defaultdict(list)
for f in sorted(glob.glob(S+'out/*.json')):
    if 'legacy' in f: continue
    d=json.load(open(f,encoding='utf-8')); parts[d['track']].append(d)
all_ids=set(); problems=[]
alltopics={t['topic_id'] for tr in road for t in road[tr]}
for track, lst in parts.items():
    theory={}; tasks=[]
    for d in lst:
        for tp in d['topics']: theory[tp['topic_id']]=tp['theory']
        tasks+=d['tasks']
    order={t['topic_id']:i for i,t in enumerate(road[track])}
    tasks.sort(key=lambda t:(order[t['topic_id']], t['task_id']))
    for t in tasks:
        if t['task_id'] in all_ids: problems.append('dup '+t['task_id'])
        all_ids.add(t['task_id'])
        if 'title' not in t:
            if t['task_id'] not in titles: problems.append('no title '+t['task_id'])
            else: t['title']=titles[t['task_id']]
    rm=[]
    for tp in road[track]:
        e=dict(tp); e['theory']=theory.get(tp['topic_id'],'')
        if not e['theory']: problems.append('no theory '+tp['topic_id'])
        for r in tp['requires']:
            if r not in alltopics: problems.append('bad require %s -> %s'%(tp['topic_id'],r))
        n=sum(1 for t in tasks if t['topic_id']==tp['topic_id'])
        if n<3: problems.append('few tasks '+tp['topic_id'])
        rm.append(e)
    out={'track':track,'roadmap':rm,'tasks':tasks}
    DST='Assets/Intern 3/Assets/Intern/Resources/Tasks/tracks/'   # игра читает направления отсюда
    json.dump(out,open(DST+'%s.json'%track,'w',encoding='utf-8'),ensure_ascii=False,indent=1)
    print(track.ljust(10),'topics',len(rm),'tasks',len(tasks),'KB',os.path.getsize(DST+'%s.json'%track)//1024)
# циклы в requires
deps={t['topic_id']:t['requires'] for tr in road for t in road[tr]}
state={}
def visit(n,stack):
    if state.get(n)==1: problems.append('cycle '+'->'.join(stack+[n])); return
    if state.get(n)==2: return
    state[n]=1
    for r in deps.get(n,[]): visit(r,stack+[n])
    state[n]=2
for n in deps: visit(n,[])
print('problems:',problems or 'нет', '| всего задач', len(all_ids))
