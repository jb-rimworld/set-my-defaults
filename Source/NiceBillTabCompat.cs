using System.Reflection;
using HarmonyLib;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Nice Bill Tab (Andromeda.NiceBillTab) caches the (search-filtered) bill list of the open Bills tab and only
    /// rebuilds it on its own actions. Bills we add or remove from code would stay invisible until the tab is
    /// closed and reopened, so after every change we raise its public <c>TabBillsDrawer.shouldRefreshFilter</c>
    /// flag (it rebuilds the cached lists on the next frame and clears the flag itself). Reflection only.
    /// </summary>
    public static class NiceBillTabCompat
    {
        private static bool _resolved;
        private static FieldInfo _refreshFlag;

        public static void RequestRefresh()
        {
            if (!_resolved)
            {
                _resolved = true;
                var drawer = AccessTools.TypeByName("NiceBillTab.TabBillsDrawer");
                var f = drawer != null ? AccessTools.Field(drawer, "shouldRefreshFilter") : null;
                if (f != null && f.IsStatic && f.FieldType == typeof(bool))
                    _refreshFlag = f;
            }
            if (_refreshFlag == null)
                return;
            try { _refreshFlag.SetValue(null, true); }
            catch (System.Exception e) { WBD_Log.Warn("Nice Bill Tab refresh failed: " + e.Message); _refreshFlag = null; }
        }
    }
}
