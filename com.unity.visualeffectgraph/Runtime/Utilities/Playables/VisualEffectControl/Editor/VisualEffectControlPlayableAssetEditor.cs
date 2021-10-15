#if VFX_HAS_TIMELINE
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;
using UnityEngine.VFX;

namespace UnityEditor.VFX
{

    [CustomTimelineEditor(typeof(VisualEffectControlPlayableAsset))]
    class VisualEffectControlPlayableAssetEditor : ClipEditor
    {
        public override void OnClipChanged(TimelineClip clip)
        {
            var behavior = clip.asset as VisualEffectControlPlayableAsset;
            if (behavior != null)
                clip.displayName = "VFX";
        }

        public static void ShadowLabel(Rect rect, GUIContent content, GUIStyle style, Color textColor, Color shadowColor)
        {
            var shadowRect = rect;
            shadowRect.xMin += 2.0f;
            shadowRect.yMin += 2.0f;
            style.normal.textColor = shadowColor;
            style.hover.textColor = shadowColor;
            GUI.Label(shadowRect, content, style);

            style.normal.textColor = textColor;
            style.hover.textColor = textColor;
            GUI.Label(rect, content, style);
        }

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            return base.GetClipOptions(clip);
        }


        private GUIStyle fontStyle = GUIStyle.none;

        private static double InverseLerp(double a, double b, double value)
        {
            return (value - a) / (b - a);
        }

        private List<VisualEffectPlayableSerializedEvent> m_CacheEventList;

        public override void DrawBackground(TimelineClip clip, ClipBackgroundRegion region)
        {
            base.DrawBackground(clip, region);
            var playable = clip.asset as VisualEffectControlPlayableAsset;

            if (playable.events == null)
                return;

            if (m_CacheEventList == null)
                m_CacheEventList = new List<VisualEffectPlayableSerializedEvent>();

            m_CacheEventList.Clear();
            var eventInRelative = VisualEffectPlayableSerializedEvent.GetEventNormalizedSpace(VisualEffectPlayableSerializedEvent.TimeSpace.AfterClipStart, playable);
            m_CacheEventList.AddRange(eventInRelative);

            var iconSize = new Vector2(8, 8);
            var startEvent = m_CacheEventList.FirstOrDefault(o => o.type == VisualEffectPlayableSerializedEvent.Type.Play);
            var stopEvent = m_CacheEventList.FirstOrDefault(o => o.type == VisualEffectPlayableSerializedEvent.Type.Stop);

            //TODOPAUL: this condition is only a security
            if (startEvent.name != null && stopEvent.name != null)
            {
                var relativeStart = InverseLerp(region.startTime, region.endTime, startEvent.time);
                var relativeStop = InverseLerp(region.startTime, region.endTime, stopEvent.time);

                var startRange = region.position.width * Mathf.Clamp01((float)relativeStart);
                var endRange = region.position.width * Mathf.Clamp01((float)relativeStop);

                var rect = new Rect(
                    region.position.x + startRange,
                    region.position.y + 0.14f * region.position.height,
                    endRange - startRange,
                    region.position.y + 0.19f * region.position.height);

                float color = 0.5f;
                EditorGUI.DrawRect(rect, Color.HSVToRGB(color, 1.0f, 1.0f));
            }

            foreach (var itEvent in m_CacheEventList)
            {
                var relativeTime = InverseLerp(region.startTime, region.endTime, itEvent.time);
                var center = new Vector2(region.position.position.x + region.position.width * (float)relativeTime,
                    region.position.position.y + region.position.height * 0.5f);

                float color = 0.5f;
                if (itEvent.type == VisualEffectPlayableSerializedEvent.Type.Custom)
                    color = 0.3f;

                var eventRect = new Rect(center - iconSize * new Vector2(1.0f, -0.5f), iconSize);
                EditorGUI.DrawRect(eventRect, Color.HSVToRGB(color, 1.0f, 1.0f));

                var textRect = new Rect(center + new Vector2(2.0f, 0), iconSize);
                ShadowLabel(textRect,
                    new GUIContent(itEvent.name),
                    fontStyle,
                    Color.HSVToRGB(color, 1.0f, 1.0f),
                    Color.HSVToRGB(color, 1.0f, 0.1f));
            }
        }
    }
}
#endif
