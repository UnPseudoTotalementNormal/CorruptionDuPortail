using System.Runtime.CompilerServices;

// Exposes Game-assembly internals to the EditMode test assembly so that
// testability hooks (e.g. RoleAttributionState.GetFrozenRolePoolOrder, the
// §3b A determinism freeze) can be asserted without widening them to public.
// [Source: project-context.md#Encapsulation — internal + InternalsVisibleTo, never public for testability]
[assembly: InternalsVisibleTo("Tests.Editor")]
