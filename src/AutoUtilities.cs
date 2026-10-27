// Project Highrise - Auto Utilities
// TheHashbrown LLC / JasbergerMusic
//
// Automatically wires every floor with every enabled utility (power, phone, cable,
// water, gas, HVAC) and extends utility-closet risers up (and down) the tower.
// Written in C# 5 so it compiles with the .NET Framework csc.exe against the
// game's own .NET 3.5 (Mono) assemblies.

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Game.Session.Board;
using Game.Session.Entities;
using Game.Session.Entities.Config;
using Game.Session.Sim;
using Game.Systems.Events;
using Game.Services.Settings;

namespace HashbrownAutoUtilities
{
    [BepInPlugin("com.thehashbrown.projecthighrise.autoutilities", "Auto Utilities", "1.0.0")]
    public class AutoUtilitiesPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> AutoRun;
        internal static ConfigEntry<int> IntervalMinutes;
        internal static ConfigEntry<KeyboardShortcut> RunNowKey;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<int> CashReserve;
        internal static ConfigEntry<bool> ExtendRisers;
        internal static ConfigEntry<bool> Power, Phone, Cable, Water, Gas, HVAC;

        void Awake()
        {
            Log = Logger;

            AutoRun = Config.Bind("General", "AutoRun", true,
                "Run automatically while the game clock is running.");
            IntervalMinutes = Config.Bind("General", "IntervalGameMinutes", 30,
                "How often (in game minutes) the automatic pass runs. A pass also runs right after any utility closet finishes construction.");
            RunNowKey = Config.Bind("General", "RunNowKey", new KeyboardShortcut(KeyCode.F7),
                "Hotkey: run a pass immediately (works while paused, even if AutoRun is off).");
            ToggleKey = Config.Bind("General", "ToggleAutoRunKey", new KeyboardShortcut(KeyCode.F7, KeyCode.LeftShift),
                "Hotkey: turn AutoRun on/off.");
            CashReserve = Config.Bind("General", "CashReserve", 1000,
                "Never spend below this much cash. Work that would dip below it is skipped and retried later.");
            ExtendRisers = Config.Bind("General", "ExtendRisers", true,
                "Copy each utility closet onto the next floor (up above ground, down in basements) so every floor gets connected. You still place the first closet on the ground floor / first basement yourself.");

            Power = Config.Bind("Utilities", "Power", true, "Auto-wire electricity.");
            Phone = Config.Bind("Utilities", "Phone", true, "Auto-wire phone lines.");
            Cable = Config.Bind("Utilities", "Cable", true, "Auto-wire TV cable.");
            Water = Config.Bind("Utilities", "Water", true, "Auto-pipe water.");
            Gas = Config.Bind("Utilities", "Gas", true, "Auto-pipe gas.");
            HVAC = Config.Bind("Utilities", "HVAC", true, "Auto-duct HVAC (only where an HVAC source exists).");

            new Harmony("com.thehashbrown.projecthighrise.autoutilities").PatchAll(typeof(AutoUtilitiesPlugin).Assembly);
            Log.LogMessage("Auto Utilities loaded. F7 = run now, Shift+F7 = toggle auto.");
        }

        void Update()
        {
            try
            {
                if (ToggleKey.Value.IsDown())
                {
                    AutoRun.Value = !AutoRun.Value;
                    Log.LogMessage("AutoRun " + (AutoRun.Value ? "ON" : "OFF"));
                    ShowToast("Auto Utilities: automatic passes " + (AutoRun.Value ? "ON" : "OFF"));
                }
                else if (RunNowKey.Value.IsDown() && Sweeper.InSession())
                {
                    Sweeper.Run("hotkey");
                }
            }
            catch (Exception ex)
            {
                Log.LogError(ex);
            }
        }

        // ---------- On-screen toast ----------
        static string _toast;
        static float _toastUntil;
        static GUIStyle _style;

        internal static void ShowToast(string text)
        {
            _toast = text;
            _toastUntil = Time.realtimeSinceStartup + 6f;
        }

        void OnGUI()
        {
            try { ClosetButton.Draw(); }
            catch (Exception ex) { Log.LogError(ex); ClosetButton.Disabled = true; }

            if (_toast == null || Time.realtimeSinceStartup > _toastUntil) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box);
                _style.fontSize = 16;
                _style.alignment = TextAnchor.MiddleCenter;
                _style.wordWrap = true;
                _style.normal.textColor = Color.white;
                _style.padding = new RectOffset(14, 14, 10, 10);
            }
            float w = 620f, h = _toast.Contains("\n") ? 66f : 42f;
            Rect r = new Rect((Screen.width - w) / 2f, 70f, w, h);
            GUI.Box(r, _toast, _style);
            GUI.Box(r, _toast, _style); // second pass darkens the default translucent box
        }

        internal static List<RangeType> EnabledTypes()
        {
            List<RangeType> list = new List<RangeType>();
            if (Power.Value) list.Add(RangeType.UtilPower);
            if (Phone.Value) list.Add(RangeType.UtilPhone);
            if (Cable.Value) list.Add(RangeType.UtilCable);
            if (Water.Value) list.Add(RangeType.UtilWater);
            if (Gas.Value) list.Add(RangeType.UtilGas);
            if (HVAC.Value) list.Add(RangeType.UtilHVAC);
            return list;
        }
    }

    // Called by the game once per in-game minute while the clock runs.
    [HarmonyPatch(typeof(InfrastructureManager), "DoMinuteUpdate")]
    public static class Patch_DoMinuteUpdate
    {
        static int _minutes;
        internal static bool Dirty = true;

        static void Postfix()
        {
            if (!AutoUtilitiesPlugin.AutoRun.Value) return;
            _minutes++;
            if (Dirty || _minutes >= Math.Max(1, AutoUtilitiesPlugin.IntervalMinutes.Value))
            {
                _minutes = 0;
                Dirty = false;
                if (Sweeper.InSession())
                {
                    try { Sweeper.Run(null); }
                    catch (Exception ex) { AutoUtilitiesPlugin.Log.LogError(ex); }
                }
            }
        }
    }

    // When a closet finishes building, run soon so the floor above it gets wired/extended.
    [HarmonyPatch(typeof(InfrastructureManager), "OnAfterConstructionFinished")]
    public static class Patch_ConstructionFinished
    {
        static void Postfix(GameEvent gev)
        {
            Entity e = global::Game.Game.ctx.entityman.Find(gev.eid);
            if (e != null && e.config.closet != null)
                Patch_DoMinuteUpdate.Dirty = true;
        }
    }
}
