using System;
using System.Collections.Generic;
using Characters;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Page order and navigation of the main menu's role book, kept pure so it is EditMode-testable without UI
    /// Toolkit. One page per role, grouped in chapters by faction (anomalies, then élus, then marginaux, like the
    /// lobby tablet); inside a chapter the roles keep the pool's order. Roles of an unknown faction are left out.
    /// </summary>
    public static class RoleBookPages
    {
        public static readonly FactionType[] ChapterOrder = { FactionType.anomaly, FactionType.chosen, FactionType.marginal };

        public static List<T> Order<T>(IEnumerable<T> roles, Func<T, FactionType> factionOf)
        {
            var pages = new List<T>();
            foreach (FactionType chapter in ChapterOrder)
                foreach (T role in roles)
                    if (role != null && factionOf(role) == chapter) pages.Add(role);
            return pages;
        }

        /// <summary>Index of the chapter's first page (a faction tab opens there), or -1 when the chapter is empty.</summary>
        public static int FirstPageOf<T>(IReadOnlyList<T> pages, Func<T, FactionType> factionOf, FactionType chapter)
        {
            for (var i = 0; i < pages.Count; i++)
                if (factionOf(pages[i]) == chapter) return i;
            return -1;
        }

        /// <summary>Turn <paramref name="delta"/> pages; a book stops at its covers (no wrap).</summary>
        public static int Turn(int current, int delta, int pageCount)
        {
            if (pageCount <= 0) return -1;
            int next = current + delta;
            return next < 0 ? 0 : next >= pageCount ? pageCount - 1 : next;
        }
    }
}
