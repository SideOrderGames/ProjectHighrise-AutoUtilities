using System;
using HarmonyLib;
using UnityEngine;
using Game.Session.Entities;
using Game.Session.Entities.Config;
using Game.UI.Session.InfoDialogs;
using G = Game.Game;

namespace HashbrownAutoUtilities
{
    // Draws an "Extend to all floors" button inside the game's Unit Report panel
    // whenever a utility closet is selected.
    internal static class ClosetButton
    {
        internal static bool Disabled;

        static readonly System.Reflection.FieldInfo F_go = AccessTools.Field(typeof(UnitInfoDialog), "_container");
        static readonly Vector3[] _corners = new Vector3[4];
        static GUIStyle _btn;
        static string _lastWhy;
        static void Why(string s)
        {
            if (s == _lastWhy) return;
            _lastWhy = s;
            AutoUtilitiesPlugin.Log.LogDebug("closet button hidden: " + s);
        }

        internal static void Draw()
        {
            if (Disabled) return;
            if (!Sweeper.InSession()) { Why("not in session"); return; }
            if (G.ctx.hud == null) { Why("hud null"); return; }
            UnitInfoDialog dlg = G.ctx.hud.unitinfo;
            if (dlg == null) { Why("dlg null"); return; }
            Entity e = G.ctx.entityman.Find(dlg.GetEid());
            if (e == null) { Why("no entity, eid " + dlg.GetEid() + " showing " + dlg.showing); return; }
            if (!dlg.showing) { Why("not showing, eid " + e.id); return; }
            if (e.config.closet == null) { Why("not closet: " + e.config.template); return; }
            if (e.config.closet.type != ClosetType.Closet) { Why("closet type " + e.config.closet.type); return; }
            if (e.components.placement == null || !e.data.placement.isbuilt) { Why("not built"); return; }

            GameObject go = F_go.GetValue(dlg) as GameObject;
            if (go == null) { Why("go null"); return; }
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt == null)
            {
                string kids = "";
                foreach (Transform c in go.transform) kids += " | " + c.name + "(" + (c is RectTransform) + "," + c.gameObject.activeInHierarchy + ")";
                Why("no RectTransform on " + go.name + " active=" + go.activeInHierarchy + kids);
                return;
            }

            // Panel rectangle in IMGUI coordinates (top-left origin).
            Canvas canvas = go.GetComponentInParent<Canvas>();
            Camera cam = (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : canvas.worldCamera;
            rt.GetWorldCorners(_corners);
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]);
            Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, _corners[2]);
            float left = bl.x, right = tr.x, bottom = Screen.height - bl.y;
            float scale = Mathf.Max(1f, Screen.height / 1080f);

            if (_btn == null)
            {
                _btn = new GUIStyle(GUI.skin.button);
                _btn.alignment = TextAnchor.MiddleCenter;
                _btn.normal.textColor = Color.white;
                _btn.hover.textColor = new Color32(255, 180, 40, 255);
            }
            _btn.fontSize = Mathf.RoundToInt(13 * scale);

            // Bottom-left of the panel, beside the game's own bulldoze/move icons.
            float w = Mathf.Min(190f * scale, (right - left) * 0.6f), h = 26f * scale, pad = 10f * scale;
            Rect r = new Rect(left + pad, bottom - h - pad * 1.4f, w, h);
            if (GUI.Button(r, "Extend to all floors", _btn))
            {
                Sweeper.ExtendFromCloset(e);
            }
        }
    }
}
