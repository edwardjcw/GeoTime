// Copyright GeoTime Contributors. All Rights Reserved.
//
// AGeoTimeTerrainActor
// --------------------
// Builds a procedural mesh from GeoTime heightmap data fetched from the backend.
// The mesh is partitioned into 64×64-cell tiles; each tile is generated
// independently so that level-of-detail streaming is possible.
//
// Workflow:
//   1. Call Initialize(Client, Meta) after fetching terrain metadata.
//   2. Call LoadHeightmap() – this triggers the async download and auto-builds
//      the mesh once the data arrives.
//   3. Use LoadTile(TileX, TileY, LOD) for on-demand streaming.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "ProceduralMeshComponent.h"
#include "GeoTimeBackendClient.h"
#include "GeoTimeTerrainActor.generated.h"

UCLASS(BlueprintType, Blueprintable)
class GEOTIMEUE_API AGeoTimeTerrainActor : public AActor
{
    GENERATED_BODY()

public:
    AGeoTimeTerrainActor();

    // ── Configuration ─────────────────────────────────────────────────────────

    /** Backend client used to fetch heightmap data. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GeoTime|Terrain")
    UGeoTimeBackendClient* BackendClient = nullptr;

    /** Terrain metadata received from the backend. */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "GeoTime|Terrain")
    FTerrainMeta TerrainMeta;

    // ── Methods ───────────────────────────────────────────────────────────────

    /**
     * Initialize the actor with metadata and a reference to the HTTP client.
     * Call this before LoadHeightmap().
     */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Terrain")
    void Initialize(UGeoTimeBackendClient* Client, const FTerrainMeta& Meta);

    /**
     * Trigger a full heightmap download and rebuild the entire terrain mesh.
     * Safe to call multiple times; the previous mesh will be replaced.
     */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Terrain")
    void LoadHeightmap();

    /**
     * Request a single tile at the specified LOD and rebuild that section.
     * @param TileX  Tile column.
     * @param TileY  Tile row.
     * @param Lod    0 = full, 1 = half, 2 = quarter resolution.
     */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Terrain")
    void LoadTile(int32 TileX, int32 TileY, int32 Lod = 0);

    /**
     * Sample the terrain height (in cm, Unreal units) at a given lat/lon.
     * Returns 0 if the heightmap has not been loaded yet.
     */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Terrain")
    float SampleHeightAtLatLon(double Lat, double Lon) const;

protected:
    virtual void BeginPlay() override;

private:
    UPROPERTY()
    UProceduralMeshComponent* ProcMesh = nullptr;

    TArray<float> HeightmapData; // flat array, GridSize×GridSize
    bool bHeightmapLoaded = false;

    // Tile size in cells.
    static constexpr int32 TileSize = 64;

    void OnHeightmapReceived(const TArray<float>& Heights);
    void OnTerrainTileReceived(const TArray<float>& TileHeights);

    /**
     * Build or rebuild the entire mesh from HeightmapData.
     * Each 64×64 block becomes one mesh section.
     */
    void BuildMesh();

    /**
     * Build one mesh section from a subregion of the heightmap.
     * @param SectionIndex  Unique index for the ProceduralMeshComponent section.
     * @param OriginX/Y     Top-left cell of the subregion.
     * @param Width/Height  Size of the subregion in cells.
     * @param Step          Sample step (1 = full, 2 = half, …)
     */
    void BuildMeshSection(int32 SectionIndex,
                          int32 OriginX, int32 OriginY,
                          int32 Width, int32 Height,
                          int32 Step = 1);

    /** Convert a normalized height value [-1, 1] to Unreal units (cm). */
    float HeightToWorldCm(float NormalizedHeight) const;

    /** Convert grid (col, row) to world XY position. */
    FVector2D GridToWorldXY(int32 Col, int32 Row) const;
};
