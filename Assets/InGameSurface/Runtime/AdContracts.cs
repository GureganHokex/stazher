using System;
using System.Collections.Generic;

namespace InGameSurface
{
    [Serializable] public sealed class Creative
    {
        public string placementId, creativeId, title, kind, mediaPath, theme;
        public bool enabled = true;
        public string Signature => JsonUtilityCompat(this);
        static string JsonUtilityCompat(Creative value) => UnityEngine.JsonUtility.ToJson(value);
    }
    [Serializable] public sealed class AdConfig { public int schemaVersion = 1; public Creative[] placements; }
    [Serializable] public sealed class AdEvent
    {
        public string eventId, sessionId, placementId, creativeId, kind, timestamp;
        public float seconds;
    }
    [Serializable] public sealed class EventBatch { public List<AdEvent> events = new List<AdEvent>(); }
    [Serializable] public sealed class EventAck { public string[] accepted; }
    [Serializable] public sealed class PixelRequest { public string url; public int attempts; }
    [Serializable] public sealed class PixelQueue { public List<PixelRequest> requests = new List<PixelRequest>(); }

}
