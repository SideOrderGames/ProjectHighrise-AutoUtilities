using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Game.Session.Board;
using Game.Session.Entities;
using Game.Session.Entities.Config;
using Game.Session.Entities.Data;
using Game.Session.Sim;
using Game.Systems.Events;
using Game.Services;
using Game.Services.Settings;
using G = Game.Game;

namespace HashbrownAutoUtilities
{
    // One pass over the whole building.
    //
    // How Project Highrise utilities work (from the game code):
    //  - Row y = -1 is the street feed: every utility is live along it.
    //  - A utility closet on floor y connects floor y to the floor below (y-1) for
    //    above-ground floors, or to the floor above (y+1) for basements.
    //  - A wire run on a floor is "live" only if it reaches a closet chain back to the feed.
    // So we go outward from the feed, floor by floor: copy closets onto the floor,
    // then fill every stretch of floor that touches a live line, then recompute
    // which lines are live before moving to the next floor.
    internal static class Sweeper
    {
        const int PIPE_FLOOR = -1;

        static readonly MethodInfo M_RecomputeActivation =
            AccessTools.Method(typeof(InfrastructureManager), "RecomputeActivation", new Type[] { typeof(RangeType) });
        static readonly MethodInfo M_UpdateClosetOnPipeChange =
            AccessTools.Method(typeof(InfrastructureManager), "UpdateClosetOnPipeChange", new Type[] { typeof(RangeType), typeof(GridPos), typeof(bool) });
        static readonly MethodInfo M_UpdateActivation =
            AccessTools.Method(typeof(InfrastructureManager), "UpdateActivation", new Type[] { typeof(RangeType) });
        static readonly FieldInfo F_NeedRecomputeClaims =
            AccessTools.Field(typeof(InfrastructureManager), "_needToRecomputeUtilityClaims");

        static int _cellsWired, _closetsPlaced, _spent, _skippedForCash, _neededCash;
        static bool _wasBlocked;

        internal static bool InSession()
        {
            return G.ctx != null && G.ctx.sim != null && G.ctx.sim.infra != null
                && G.ctx.board != null && G.ctx.board.grid != null
                && G.ctx.sim.infra.IsInfrastructureInitialized;
        }

        internal static void Run(string reason)
        {
            List<RangeType> types = AutoUtilitiesPlugin.EnabledTypes();
            if (types.Count == 0) return;

            _cellsWired = _closetsPlaced = _spent = _skippedForCash = _neededCash = 0;
            Grid grid = G.ctx.board.grid;
            InfrastructureManager infra = G.ctx.sim.infra;

            // Make sure activation state is current before we read it.
            foreach (RangeType t in types) Recompute(infra, t);

            // Above ground: 0, 1, 2 ... top. Closets on floor y come from floor y-1.
            for (int y = 0; y < grid.maptop; y++)
            {
                if (y >= 1 && AutoUtilitiesPlugin.ExtendRisers.Value) ExtendRisers(grid, types, y - 1, y);
                foreach (RangeType t in types) { if (WireFloor(grid, infra, t, y)) Recompute(infra, t); }
            }
            // Basements: -2, -3 ... bottom. Closets on floor y come from floor y+1.
            for (int y = PIPE_FLOOR - 1; y >= grid.mapbottom; y--)
            {
                if (y <= PIPE_FLOOR - 2 && AutoUtilitiesPlugin.ExtendRisers.Value) ExtendRisers(grid, types, y + 1, y);
                foreach (RangeType t in types) { if (WireFloor(grid, infra, t, y)) Recompute(infra, t); }
            }

            if (_cellsWired > 0 || _closetsPlaced > 0)
            {
                // UpdateActivation = recompute live lines + redraw pipes for that type.
                foreach (RangeType t in types) M_UpdateActivation.Invoke(infra, new object[] { t });
                F_NeedRecomputeClaims.SetValue(infra, true);
                infra.stats.RecomputeAvailable();
            }

            int cash = G.ctx.sim.player.cash;
            string msg;
            if (_cellsWired > 0 || _closetsPlaced > 0)
                msg = string.Format("Auto Utilities: wired {0} cells, placed {1} closets, spent ${2:N0}", _cellsWired, _closetsPlaced, -_spent);
            else if (_skippedForCash > 0)
                msg = "Auto Utilities: nothing done";
            else
                msg = "Auto Utilities: everything reachable is already wired";
            if (_skippedForCash > 0)
                msg += string.Format("\n{0} jobs waiting on cash: need ${1:N0} more (cash ${2:N0}, reserve ${3:N0})",
                    _skippedForCash, Math.Max(0, AutoUtilitiesPlugin.CashReserve.Value + _neededCash - cash), cash, AutoUtilitiesPlugin.CashReserve.Value);

            bool newlyBlocked = _skippedForCash > 0 && !_wasBlocked;
            _wasBlocked = _skippedForCash > 0;
            if (_cellsWired > 0 || _closetsPlaced > 0 || reason != null || newlyBlocked)
            {
                AutoUtilitiesPlugin.Log.LogMessage((reason != null ? "[" + reason + "] " : "") + msg.Replace("\n", " | "));
                AutoUtilitiesPlugin.ShowToast(msg);
            }
        }

        static void Recompute(InfrastructureManager infra, RangeType t)
        {
            M_RecomputeActivation.Invoke(infra, new object[] { t });
        }

        static bool TrySpend(int cost)  // cost is negative, game convention
        {
            if (cost == 0) return true;
            Player player = G.ctx.sim.player;
            int cash = player.cash;
            if (!player.CanSpend(cost) || cash + cost < AutoUtilitiesPlugin.CashReserve.Value)
            {
                _skippedForCash++;
                _neededCash += -cost;
                return false;
            }
            player.DoAdd(cost, Reason.BuildCost);
            _spent += cost;
            return true;
        }

        static int PipeCellCost(RangeType t)
        {
            InfraPipeDefinition def = G.serv.globals.settings.infra.FindPipeDef(t);
            if (def == null) return 0;
            try { return (int)(Money)def.buildcost.Evaluate(null); }
            catch (Exception) { return (int)(Money)def.buildcost.EvaluateGameStateOnly(); }
        }

        // ---------- Wires ----------

        // Returns true if anything was placed on this floor.
        static bool WireFloor(Grid grid, InfrastructureManager infra, RangeType t, int y)
        {
            bool changed = false;
            RangeTracker ranges = G.ctx.board.ranges;
            int x = grid.mapleft;
            while (x < grid.mapright)
            {
                GridCell c = grid.FindGridCellOrNull(new GridPos(x, y));
                if (c == null || !c.hasFloor) { x++; continue; }

                // Contiguous stretch of floor [start, end].
                int start = x;
                while (x < grid.mapright)
                {
                    GridCell n = grid.FindGridCellOrNull(new GridPos(x, y));
                    if (n == null || !n.hasFloor) break;
                    x++;
                }
                int end = x - 1;

                bool live = false;
                List<GridPos> missing = new List<GridPos>();
                for (int i = start; i <= end; i++)
                {
                    GridPos p = new GridPos(i, y);
                    if (ranges.FindConnectedRangeThatContains(t, p) != null) live = true;
                    if (ranges.FindRangeThatContains(t, p) == null) missing.Add(p);
                }
                if (!live || missing.Count == 0) continue;

                if (!TrySpend(PipeCellCost(t) * missing.Count)) continue;

                foreach (GridPos p in missing)
                {
                    ranges.EnsureInARange(t, p);
                    // Hooks up any closet directly above/below this cell, same as a manual placement.
                    M_UpdateClosetOnPipeChange.Invoke(infra, new object[] { t, p, true });
                }
                _cellsWired += missing.Count;
                changed = true;
            }
            return changed;
        }

        // ---------- Risers (utility closets) ----------

        static bool ProvidesEnabled(Entity e, List<RangeType> types)
        {
            if (e.config.closet == null || e.config.closet.type != ClosetType.Closet) return false;
            if (e.config.closet.provides == null) return false;
            foreach (UtilityProvidedPair p in e.config.closet.provides)
                if (types.Contains(p.type)) return true;
            return false;
        }

        static void ExtendRisers(Grid grid, List<RangeType> types, int fromY, int toY)
        {
            HashSet<int> seen = new HashSet<int>();
            for (int x = grid.mapleft; x < grid.mapright; x++)
            {
                Entity src = grid.FindEntityOrNull(new GridPos(x, fromY));
                if (src == null || !seen.Add(src.id)) continue;
                if (!ProvidesEnabled(src, types)) continue;

                GridPos root = src.data.placement.gridpos;
                if (root.y != fromY) continue;
                TryPlaceCloset(grid, src.config, new GridPos(root.x, toY));
            }
        }

        internal enum PlaceResult { Placed, AlreadyThere, Blocked, NoCash }

        // Places a copy of a closet template at target, paying normal build cost.
        static PlaceResult TryPlaceCloset(Grid grid, EntityTemplate tmpl, GridPos target)
        {
            Entity existing = grid.FindEntityOrNull(target);
            if (existing != null)
            {
                return (existing.config == tmpl && existing.data.placement.gridpos.Equals(target))
                    ? PlaceResult.AlreadyThere : PlaceResult.Blocked;
            }

            // Needs floor under the whole footprint; keep away from lobbies (extra game bookkeeping there).
            for (int i = -1; i <= tmpl.placement.size.x; i++)
            {
                GridCell gc = grid.FindGridCellOrNull(target.Add(i, 0));
                bool inFootprint = i >= 0 && i < tmpl.placement.size.x;
                if (inFootprint && (gc == null || !gc.hasFloor)) return PlaceResult.Blocked;
                if (gc != null && gc.hasLobby) return PlaceResult.Blocked;
            }

            if (grid.CanInsertIntoFootprint(target, tmpl, tmpl.placement.reqs) != GridCell.GridAddResult.Success)
                return PlaceResult.Blocked;

            int cost = UnitConfig.GetBuildCost(target, tmpl);
            if (!TrySpend(cost)) return PlaceResult.NoCash;

            Entity e = G.ctx.board.CreateEntity(tmpl, PlacementData.INVALID_POS_VECTOR, false);
            PlacementComponentHelper.Place(e, target);
            G.ctx.events.Send(GameEventType.EntityAfterCreatedByPlayer, e.id, target);
            _closetsPlaced++;
            return PlaceResult.Placed;
        }

        // Walks from startY in direction dy, copying the closet onto each floor.
        // Stops at the first floor where it can't go (no floor, room in the way, out of cash).
        static void ExtendColumn(Grid grid, EntityTemplate tmpl, int x, int startY, int dy, ref int placed, ref string stopReason)
        {
            for (int y = startY; y < grid.maptop && y >= grid.mapbottom; y += dy)
            {
                if (y == PIPE_FLOOR) break;
                PlaceResult r = TryPlaceCloset(grid, tmpl, new GridPos(x, y));
                if (r == PlaceResult.Placed) { placed++; continue; }
                if (r == PlaceResult.AlreadyThere) continue;
                GridCell gc = grid.FindGridCellOrNull(new GridPos(x, y));
                bool noFloor = gc == null || !gc.hasFloor;
                if (r == PlaceResult.NoCash) stopReason = "out of cash";
                else if (!noFloor && stopReason == null) stopReason = "blocked on floor " + (y >= 0 ? (y + 1).ToString() : "B" + (-y - 1));
                break;
            }
        }

        // Closet button: copy this closet up to the top of the tower and down through the basements.
        // Above ground a closet links its floor to the one below; in basements to the one above;
        // so from any closet we fill upward above ground and downward from the first basement.
        internal static void ExtendFromCloset(Entity closet)
        {
            if (closet == null || closet.config.closet == null) return;
            Grid grid = G.ctx.board.grid;
            EntityTemplate tmpl = closet.config;
            GridPos root = closet.data.placement.gridpos;

            _cellsWired = _closetsPlaced = _spent = _skippedForCash = _neededCash = 0;
            int up = 0, down = 0;
            string stopUp = null, stopDown = null;

            if (root.y >= 0)
            {
                ExtendColumn(grid, tmpl, root.x, root.y + 1, 1, ref up, ref stopUp);
                ExtendColumn(grid, tmpl, root.x, PIPE_FLOOR - 1, -1, ref down, ref stopDown);
            }
            else
            {
                ExtendColumn(grid, tmpl, root.x, root.y - 1, -1, ref down, ref stopDown);
                ExtendColumn(grid, tmpl, root.x, 0, 1, ref up, ref stopUp);
            }

            // Wire whatever is already live; the new closets get wired automatically as they finish building.
            Patch_DoMinuteUpdate.Dirty = true;

            string name = Loc.Get(tmpl.unit.locname);
            string msg = string.Format("{0}: placed {1} up, {2} down, spent ${3:N0}", name, up, down, -_spent);
            string why = "";
            if (stopUp != null) why += "Up stopped: " + stopUp + ". ";
            if (stopDown != null) why += "Down stopped: " + stopDown + ". ";
            if (_skippedForCash > 0)
                why += string.Format("Need ${0:N0} more (reserve ${1:N0}).", Math.Max(0, AutoUtilitiesPlugin.CashReserve.Value + _neededCash - G.ctx.sim.player.cash), AutoUtilitiesPlugin.CashReserve.Value);
            if (up + down == 0 && why == "") why = "Every floor in this column already has one.";
            if (why != "") msg += "\n" + why.Trim();
            AutoUtilitiesPlugin.Log.LogMessage("[closet button] " + msg.Replace("\n", " | "));
            AutoUtilitiesPlugin.ShowToast(msg);
        }
    }

    internal static class PlacementComponentHelper
    {
        // Mirrors BuyEntityInputMode.PlaceCursorEntity.
        internal static void Place(Entity e, GridPos pos)
        {
            var placement = e.components.placement;
            placement.Insert(pos);
            e.enabled = true;
            e.components.sprite.ClearLayerOverride();
            placement.StartBuilding(placement.NeedsBuiltInstantly());
        }
    }
}
