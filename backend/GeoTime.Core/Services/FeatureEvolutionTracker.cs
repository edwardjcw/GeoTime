using GeoTime.Core.Models;

namespace GeoTime.Core.Services;

/// <summary>
/// Phase L4: Tracks feature changes tick-by-tick and maintains a full temporal
/// biography for every <see cref="DetectedFeature"/> in the
/// <see cref="FeatureRegistry"/>.
/// <para>
/// Called at the end of every <c>AdvanceSimulation</c> tick after
/// <see cref="FeatureDetectorService.Detect"/> has produced a fresh snapshot
/// registry.  Compares the new snapshot against the previous persistent
/// registry and generates one of:
/// </para>
/// <list type="table">
/// <item><term>FEATURE_BORN</term><description>New connected component detected.</description></item>
/// <item><term>FEATURE_EXTINCT</term><description>Component no longer present.</description></item>
/// <item><term>FEATURE_SPLIT</term><description>One component split into two.</description></item>
/// <item><term>FEATURE_MERGE</term><description>Two components merged into one.</description></item>
/// <item><term>AREA_SHIFT_MAJOR</term><description>Area changed by &gt; 20 %.</description></item>
/// <item><term>SUBMERGENCE</term><description>Continental region dropped below sea level.</description></item>
/// <item><term>EXPOSURE</term><description>Oceanic region rose above sea level.</description></item>
/// </list>
/// </summary>
public sealed class FeatureEvolutionTracker
{
    // ── Thresholds ─────────────────────────────────────────────────────────────

    /// <summary>Minimum fractional area change to trigger AREA_SHIFT_MAJOR.</summary>
    private const float AreaShiftThreshold = 0.20f;

    /// <summary>
    /// Minimum cell-index overlap fraction to consider two features "the same"
    /// (used when IDs don't match — handles grid-ordering changes and splits).
    /// </summary>
    private const float CellOverlapFraction = 0.30f;

    /// <summary>Tick interval at which an age-honorific suffix may be appended.</summary>
    private const long AgeMilestoneTicks = 500L;

    // ── Entry point ────────────────────────────────────────────────────────────

    /// <summary>
    /// Merge <paramref name="currentRegistry"/> (freshly detected this tick) with
    /// <paramref name="previousRegistry"/> (the persistent registry from the
    /// previous tick), recording change events and evolving names as appropriate.
    /// The merged registry replaces <paramref name="state"/>.FeatureRegistry.
    /// </summary>
    public void Track(
        SimulationState state,
        FeatureRegistry previousRegistry,
        FeatureRegistry currentRegistry,
        long currentTick)
    {
        // Build a cell → feature-id lookup for both registries so overlap analysis
        // is O(N) rather than O(N²).
        var prevCellIndex  = BuildCellIndex(previousRegistry);
        var currCellIndex  = BuildCellIndex(currentRegistry);

        var matchedPrev = new HashSet<string>();
        var matchedCurr = new HashSet<string>();

        // ── Pass 1: Match by ID (same ID = same feature slot) ─────────────────

        foreach (var (id, currFeat) in currentRegistry.Features)
        {
            if (!previousRegistry.Features.TryGetValue(id, out var prevFeat)) continue;

            matchedPrev.Add(id);
            matchedCurr.Add(id);

            MergeHistory(prevFeat, currFeat, currentTick, state);
        }

        // ── Pass 2: Detect splits (old unmatched → 2+ new features share its cells) ─

        foreach (var (prevId, prevFeat) in previousRegistry.Features)
        {
            if (matchedPrev.Contains(prevId)) continue;

            // Find current features that contain cells formerly in prevFeat.
            // Use type-group filtering to avoid cross-category false positives:
            // e.g., a single-cell lake falsely matching a continent.
            var overlapping = FindOverlappingFeatures(prevFeat, currCellIndex, currentRegistry,
                GetTypeGroup(prevFeat.Type));

            if (overlapping.Count >= 2)
            {
                // SPLIT: close the parent, tag children with SplitFromId.
                var closingSnap = prevFeat.Current with
                {
                    SimTickExtinct = currentTick,
                    Status = FeatureStatus.Extinct,
                };
                var closed = CloseFeature(prevFeat, closingSnap);
                closed.Metrics["event_split_tick"] = currentTick;
                currentRegistry.Features[prevId] = closed;
                matchedPrev.Add(prevId);

                // The child with the most cells keeps the parent name; the others
                // get directional prefixes.
                var sorted = overlapping
                    .OrderByDescending(id => OverlapCount(prevFeat.CellIndices, currentRegistry.Features[id].CellIndices))
                    .ToList();

                for (int k = 0; k < sorted.Count; k++)
                {
                    var childId   = sorted[k];
                    var childFeat = currentRegistry.Features[childId];
                    var oldSnap   = childFeat.History[^1];

                    string evolvedName = k == 0
                        ? prevFeat.Current.Name // largest child keeps parent name
                        : FeatureNameGenerator.Evolve(prevFeat.Current.Name,
                            NameChangeReason.Split, 0u, prevFeat.Type, k);

                    var newSnap = oldSnap with
                    {
                        SimTickCreated = currentTick,
                        Name           = evolvedName,
                        SplitFromId    = prevId,
                    };
                    childFeat.History.Clear();
                    childFeat.History.Add(newSnap);
                    matchedCurr.Add(childId);
                }
            }
            else if (overlapping.Count == 1)
            {
                // Feature renamed / area-shifted so much it got a new index slot;
                // treat as the same feature (carry over history).
                var singleId   = overlapping[0];
                var singleFeat = currentRegistry.Features[singleId];
                if (!matchedCurr.Contains(singleId))
                {
                    MergeHistory(prevFeat, singleFeat, currentTick, state);
                    matchedPrev.Add(prevId);
                    matchedCurr.Add(singleId);
                }
                else
                {
                    // Already matched; just close the previous slot.
                    CloseAndAddExtinct(prevId, prevFeat, currentTick, currentRegistry);
                    matchedPrev.Add(prevId);
                }
            }
            else
            {
                // No overlap at all → truly extinct.
                CloseAndAddExtinct(prevId, prevFeat, currentTick, currentRegistry);
                matchedPrev.Add(prevId);
            }
        }

        // ── Pass 3: Detect merges and split-children from unmatched new features ──

        foreach (var (currId, currFeat) in currentRegistry.Features)
        {
            if (matchedCurr.Contains(currId)) continue;
            if (currFeat.Current.Status == FeatureStatus.Extinct) continue;

            // Find all old features whose cells now lie inside this current feature.
            // Use type-group filtering to avoid cross-category false matches.
            var absorbedOld = FindAbsorbedFeatures(currFeat, prevCellIndex, previousRegistry,
                GetTypeGroup(currFeat.Type));

            if (absorbedOld.Count >= 2)
            {
                // MERGE: close all absorbed parents, give current a portmanteau name.
                string mergedName = currFeat.Current.Name;
                var first = previousRegistry.Features[absorbedOld[0]];
                mergedName = FeatureNameGenerator.Evolve(first.Current.Name,
                    NameChangeReason.Merge, 0u, first.Type, 0);

                foreach (var absId in absorbedOld)
                {
                    if (!matchedPrev.Contains(absId))
                    {
                        var absFeat    = previousRegistry.Features[absId];
                        var closingSnap = absFeat.Current with
                        {
                            SimTickExtinct = currentTick,
                            Status = FeatureStatus.Extinct,
                            MergedIntoId = currId,
                        };
                        var closed = CloseFeature(absFeat, closingSnap);
                        closed.Metrics["event_merge_tick"] = currentTick;
                        currentRegistry.Features[absId] = closed;
                        matchedPrev.Add(absId);
                    }
                }

                var oldSnap = currFeat.History[^1];
                currFeat.History.Clear();
                currFeat.History.Add(oldSnap with { Name = mergedName });
            }
            else if (absorbedOld.Count == 1)
            {
                // Single-parent overlap: this new feature is a SPLIT child.
                // The parent already matched by ID (or will be closed above).
                // Tag this child with the parent's ID so history queries can trace lineage.
                var parentId = absorbedOld[0];
                if (previousRegistry.Features.TryGetValue(parentId, out var parentFeat))
                {
                    // Use a directional split name (child B gets evolved name).
                    string splitName = FeatureNameGenerator.Evolve(parentFeat.Current.Name,
                        NameChangeReason.Split, 0u, parentFeat.Type, 1);
                    var oldSnap = currFeat.History[^1];
                    currFeat.History.Clear();
                    currFeat.History.Add(oldSnap with
                    {
                        SimTickCreated = currentTick,
                        Name           = splitName,
                        SplitFromId    = parentId,
                    });
                }
            }

            matchedCurr.Add(currId);
        }

        // ── Pass 4: Any remaining unmatched current features are FEATURE_BORN ──
        // (already have their initial snapshot from the detector — no action needed)

        currentRegistry.LastUpdatedTick = currentTick;
        state.FeatureRegistry = currentRegistry;
    }

    // ── History merging ────────────────────────────────────────────────────────

    /// <summary>
    /// Carry the full history from <paramref name="prevFeat"/> into
    /// <paramref name="currFeat"/> and append a new snapshot only when the
    /// feature has meaningfully changed.
    /// </summary>
    private static void MergeHistory(
        DetectedFeature prevFeat,
        DetectedFeature currFeat,
        long currentTick,
        SimulationState state)
    {
        var prevSnap = prevFeat.Current;
        var currSnap = currFeat.History[^1]; // single fresh snapshot from detector

        // Snapshot the old history before potentially clearing it.
        // prevFeat and currFeat may share the same History list reference when the
        // caller recycles feature objects (e.g. in unit tests).
        var oldHistory = prevFeat.History.ToList();

        // Restore full prior history.
        currFeat.History.Clear();
        foreach (var s in oldHistory) currFeat.History.Add(s);

        // Determine what kind of change occurred, if any.
        var (changed, newStatus, nameReason) = ClassifyChange(prevSnap, currSnap, currentTick);

        if (!changed)
        {
            // Nothing significant: leave history unchanged (no new snapshot added).
            return;
        }

        // Evolve the name if needed.
        string name = prevSnap.Name;
        if (nameReason.HasValue)
            name = FeatureNameGenerator.Evolve(prevSnap.Name, nameReason.Value, 0u, prevFeat.Type, 0);

        // Age milestone honorific.
        long age = currentTick - prevFeat.History[0].SimTickCreated;
        if (age > 0 && age % AgeMilestoneTicks == 0)
            name = FeatureNameGenerator.Evolve(name, NameChangeReason.RenameByAge, 0u, prevFeat.Type, (int)(age / AgeMilestoneTicks));

        currFeat.History.Add(currSnap with
        {
            SimTickCreated = currentTick,
            Name           = name,
            Status         = newStatus,
        });
    }

    /// <summary>
    /// Analyses the transition between <paramref name="prev"/> and
    /// <paramref name="curr"/> snapshots and returns whether a meaningful
    /// change occurred.
    /// </summary>
    private static (bool changed, FeatureStatus status, NameChangeReason? reason)
        ClassifyChange(FeatureSnapshot prev, FeatureSnapshot curr, long currentTick)
    {
        // Submergence: feature was above sea level, area has shrunk dramatically.
        if (prev.Status is FeatureStatus.Active or FeatureStatus.Nascent
         && curr.Status == FeatureStatus.Submerged)
            return (true, FeatureStatus.Submerged, NameChangeReason.Submergence);

        // Exposure: feature was submerged, now above sea level.
        if (prev.Status == FeatureStatus.Submerged
         && curr.Status is FeatureStatus.Active or FeatureStatus.Exposed)
            return (true, FeatureStatus.Exposed, NameChangeReason.Exposure);

        // Major area shift (> 20%).
        if (prev.AreaKm2 > 0f)
        {
            float areaDelta = MathF.Abs(curr.AreaKm2 - prev.AreaKm2) / prev.AreaKm2;
            if (areaDelta > AreaShiftThreshold)
                return (true, curr.Status, null);
        }

        // Significant centroid movement (> 5°).
        float latDelta = MathF.Abs(curr.CenterLat - prev.CenterLat);
        float lonDelta = MathF.Abs(curr.CenterLon - prev.CenterLon);
        if (latDelta > 5f || lonDelta > 5f)
            return (true, curr.Status, null);

        return (false, curr.Status, null);
    }

    // ── Extinction helpers ────────────────────────────────────────────────────

    private static DetectedFeature CloseFeature(DetectedFeature feat, FeatureSnapshot closingSnap)
    {
        // Create a new feature object that mirrors the old one but with a closing snapshot.
        // We must NOT modify prevFeat in-place because the caller may iterate over prevRegistry.
        var closed = new DetectedFeature
        {
            Id   = feat.Id,
            Type = feat.Type,
        };
        foreach (var s   in feat.History)            closed.History.Add(s);
        foreach (var p   in feat.AssociatedPlateIds) closed.AssociatedPlateIds.Add(p);
        foreach (var ci  in feat.CellIndices)        closed.CellIndices.Add(ci);
        foreach (var (k, v) in feat.Metrics)         closed.Metrics[k] = v;

        // Replace the final snapshot with the closing one.
        if (closed.History.Count > 0 && closed.History[^1].SimTickExtinct == long.MaxValue)
        {
            closed.History.RemoveAt(closed.History.Count - 1);
            closed.History.Add(closingSnap);
        }
        else
        {
            closed.History.Add(closingSnap);
        }
        return closed;
    }

    private static void CloseAndAddExtinct(
        string id,
        DetectedFeature prevFeat,
        long currentTick,
        FeatureRegistry currentRegistry)
    {
        var closingSnap = prevFeat.Current with
        {
            SimTickExtinct = currentTick,
            Status         = FeatureStatus.Extinct,
        };
        var closed = CloseFeature(prevFeat, closingSnap);
        closed.Metrics["event_extinct_tick"] = currentTick;
        currentRegistry.Features[id] = closed;
    }

    // ── Cell-overlap analysis ─────────────────────────────────────────────────

    /// <summary>Builds a mapping of cell-index → feature-id for the given registry.</summary>
    /// <remarks>
    /// Only primary geographic features (continents, oceans, islands, mountain ranges,
    /// tectonic plates) are indexed.  Hydrological features (rivers, lakes) and
    /// atmospheric features share cells with these primaries and would skew the
    /// overlap calculations used for split / merge detection.
    /// </remarks>
    private static readonly HashSet<FeatureType> CellIndexTypes = [
        FeatureType.Continent, FeatureType.Ocean, FeatureType.Sea, FeatureType.Island,
        FeatureType.IslandChain, FeatureType.MountainRange, FeatureType.TectonicPlate,
        FeatureType.Rift, FeatureType.SubductionZone, FeatureType.ImpactBasin,
    ];

    private static Dictionary<int, string> BuildCellIndex(FeatureRegistry registry)
    {
        var index = new Dictionary<int, string>();
        foreach (var (id, feat) in registry.Features)
        {
            if (feat.Current.Status == FeatureStatus.Extinct) continue;
            if (!CellIndexTypes.Contains(feat.Type)) continue; // skip hydro/atmo features
            foreach (var ci in feat.CellIndices)
                index.TryAdd(ci, id); // first writer wins
        }
        return index;
    }

    // ── Type-group compatibility ───────────────────────────────────────────────

    /// <summary>
    /// Returns the type "group" for compatibility checking.  Features within the
    /// same group can match each other across ticks (e.g., Sea and Ocean are both
    /// water bodies, so they can match as one shrinks/grows into the other).
    /// Returns <c>null</c> for feature types that are not tracked for split / merge.
    /// </summary>
    private static FeatureType[]? GetTypeGroup(FeatureType type) => type switch
    {
        FeatureType.Continent or FeatureType.Island or FeatureType.IslandChain
            => [FeatureType.Continent, FeatureType.Island, FeatureType.IslandChain],
        FeatureType.Ocean or FeatureType.Sea or FeatureType.InlandSea
            => [FeatureType.Ocean, FeatureType.Sea, FeatureType.InlandSea],
        FeatureType.MountainRange => [FeatureType.MountainRange],
        FeatureType.Rift          => [FeatureType.Rift],
        FeatureType.SubductionZone => [FeatureType.SubductionZone],
        FeatureType.TectonicPlate  => [FeatureType.TectonicPlate],
        _ => null, // rivers, lakes, atmospheric features: skip split/merge analysis
    };

    /// <summary>
    /// Returns IDs of current features that contain ≥ <see cref="CellOverlapFraction"/>
    /// of <paramref name="prevFeat"/>'s cells.
    /// </summary>
    /// <param name="typeGroup">
    /// When provided, only returns current features whose type is in this group.
    /// Pass <c>null</c> to skip the feature in Pass 2 (returns empty list).
    /// </param>
    private static List<string> FindOverlappingFeatures(
        DetectedFeature prevFeat,
        Dictionary<int, string> currCellIndex,
        FeatureRegistry currentRegistry,
        FeatureType[]? typeGroup)
    {
        if (prevFeat.CellIndices.Count == 0) return [];
        if (typeGroup == null) return []; // skip split/merge for this feature type

        var counts = new Dictionary<string, int>();
        foreach (var ci in prevFeat.CellIndices)
        {
            if (currCellIndex.TryGetValue(ci, out var cid))
                counts[cid] = counts.GetValueOrDefault(cid) + 1;
        }

        int minOverlap = Math.Max(1, (int)(prevFeat.CellIndices.Count * CellOverlapFraction));
        return counts
            .Where(kv => kv.Value >= minOverlap
                      && currentRegistry.Features.TryGetValue(kv.Key, out var cf)
                      && typeGroup.Contains(cf.Type))
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>
    /// Returns IDs of previous features where ≥ <see cref="CellOverlapFraction"/>
    /// of their cells now lie inside <paramref name="currFeat"/>.
    /// </summary>
    private static List<string> FindAbsorbedFeatures(
        DetectedFeature currFeat,
        Dictionary<int, string> prevCellIndex,
        FeatureRegistry previousRegistry,
        FeatureType[]? typeGroup = null)
    {
        if (currFeat.CellIndices.Count == 0) return [];
        if (typeGroup == null) return []; // skip merge/split analysis for this type

        var counts = new Dictionary<string, int>();
        foreach (var ci in currFeat.CellIndices)
        {
            if (prevCellIndex.TryGetValue(ci, out var pid))
                counts[pid] = counts.GetValueOrDefault(pid) + 1;
        }

        var result = new List<string>();
        foreach (var (pid, cnt) in counts)
        {
            if (!previousRegistry.Features.TryGetValue(pid, out var prevFeat)) continue;
            if (prevFeat.CellIndices.Count == 0) continue;
            if (!typeGroup.Contains(prevFeat.Type)) continue; // only same type-group
            float fraction = (float)cnt / prevFeat.CellIndices.Count;
            if (fraction >= CellOverlapFraction) result.Add(pid);
        }
        return result;
    }

    private static int OverlapCount(List<int> a, List<int> b)
    {
        var setB = new HashSet<int>(b);
        return a.Count(ci => setB.Contains(ci));
    }
}
