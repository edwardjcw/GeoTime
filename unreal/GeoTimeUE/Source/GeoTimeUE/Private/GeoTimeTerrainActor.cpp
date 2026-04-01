// Copyright GeoTime Contributors. All Rights Reserved.

#include "GeoTimeTerrainActor.h"
#include "ProceduralMeshComponent.h"

AGeoTimeTerrainActor::AGeoTimeTerrainActor()
{
    PrimaryActorTick.bCanEverTick = false;

    ProcMesh = CreateDefaultSubobject<UProceduralMeshComponent>(TEXT("TerrainMesh"));
    RootComponent = ProcMesh;
    ProcMesh->bUseAsyncCooking = true; // cook collision off the game thread
}

// ─────────────────────────────────────────────────────────────────────────────

void AGeoTimeTerrainActor::BeginPlay()
{
    Super::BeginPlay();
}

// ─────────────────────────────────────────────────────────────────────────────

void AGeoTimeTerrainActor::Initialize(UGeoTimeBackendClient* Client,
                                       const FTerrainMeta& Meta)
{
    BackendClient = Client;
    TerrainMeta   = Meta;
}

// ─────────────────────────────────────────────────────────────────────────────

void AGeoTimeTerrainActor::LoadHeightmap()
{
    if (!BackendClient)
    {
        UE_LOG(LogTemp, Warning,
            TEXT("GeoTimeTerrainActor: BackendClient not set"));
        return;
    }

    BackendClient->OnHeightmapReceived.BindUObject(
        this, &AGeoTimeTerrainActor::OnHeightmapReceived);
    BackendClient->FetchHeightmapRaw();
}

void AGeoTimeTerrainActor::OnHeightmapReceived(const TArray<float>& Heights)
{
    HeightmapData   = Heights;
    bHeightmapLoaded = true;
    BuildMesh();
}

// ─────────────────────────────────────────────────────────────────────────────

void AGeoTimeTerrainActor::LoadTile(int32 TileX, int32 TileY, int32 Lod)
{
    if (!BackendClient)
    {
        UE_LOG(LogTemp, Warning,
            TEXT("GeoTimeTerrainActor: BackendClient not set"));
        return;
    }

    BackendClient->OnTerrainTileReceived.BindUObject(
        this, &AGeoTimeTerrainActor::OnTerrainTileReceived);
    BackendClient->FetchTerrainTile(TileX, TileY, Lod);
}

void AGeoTimeTerrainActor::OnTerrainTileReceived(
    const TArray<float>& /*TileHeights*/)
{
    // In a streaming implementation the caller would supply TileX/TileY
    // through a closure or a pending-request queue.  For simplicity the
    // full-heightmap path is the primary route; tile streaming is a
    // future optimization.
    UE_LOG(LogTemp, Log,
        TEXT("GeoTimeTerrainActor: terrain tile received (%d floats)"),
        0 /* TileHeights.Num() – unavailable without capture */);
}

// ─────────────────────────────────────────────────────────────────────────────

float AGeoTimeTerrainActor::SampleHeightAtLatLon(double Lat, double Lon) const
{
    if (!bHeightmapLoaded) return 0.f;

    const int32 GridSize = TerrainMeta.GridSize;

    // Map lat/lon to grid col/row (equirectangular projection)
    const double NormLon = (Lon + 180.0) / 360.0;  // [0,1]
    const double NormLat = (90.0 - Lat) / 180.0;   // [0,1], north = 0

    const int32 Col = FMath::Clamp(
        (int32)(NormLon * GridSize), 0, GridSize - 1);
    const int32 Row = FMath::Clamp(
        (int32)(NormLat * GridSize), 0, GridSize - 1);

    const float NormHeight = HeightmapData[Row * GridSize + Col];
    return HeightToWorldCm(NormHeight);
}

// ─────────────────────────────────────────────────────────────────────────────

void AGeoTimeTerrainActor::BuildMesh()
{
    if (!bHeightmapLoaded) return;

    const int32 GridSize = TerrainMeta.GridSize;
    const int32 NumTilesX = FMath::CeilToInt((float)GridSize / TileSize);
    const int32 NumTilesY = FMath::CeilToInt((float)GridSize / TileSize);

    ProcMesh->ClearAllMeshSections();

    for (int32 TY = 0; TY < NumTilesY; ++TY)
    {
        for (int32 TX = 0; TX < NumTilesX; ++TX)
        {
            const int32 OriginX = TX * TileSize;
            const int32 OriginY = TY * TileSize;
            const int32 Width   = FMath::Min(TileSize, GridSize - OriginX);
            const int32 Height  = FMath::Min(TileSize, GridSize - OriginY);

            BuildMeshSection(TY * NumTilesX + TX,
                             OriginX, OriginY,
                             Width, Height, /*Step=*/1);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────

void AGeoTimeTerrainActor::BuildMeshSection(int32 SectionIndex,
                                             int32 OriginX, int32 OriginY,
                                             int32 Width, int32 Height,
                                             int32 Step)
{
    const int32 GridSize = TerrainMeta.GridSize;
    const int32 Cols = FMath::CeilToInt((float)Width  / Step) + 1;
    const int32 Rows = FMath::CeilToInt((float)Height / Step) + 1;

    TArray<FVector>  Vertices;
    TArray<int32>    Triangles;
    TArray<FVector>  Normals;
    TArray<FVector2D> UVs;
    TArray<FColor>   VertexColors;

    Vertices.Reserve(Cols * Rows);
    UVs.Reserve(Cols * Rows);
    Normals.Reserve(Cols * Rows);
    Triangles.Reserve((Cols - 1) * (Rows - 1) * 6);

    for (int32 Row = 0; Row < Rows; ++Row)
    {
        for (int32 Col = 0; Col < Cols; ++Col)
        {
            const int32 GridCol = FMath::Min(OriginX + Col * Step, GridSize - 1);
            const int32 GridRow = FMath::Min(OriginY + Row * Step, GridSize - 1);

            const float NormHeight =
                HeightmapData[GridRow * GridSize + GridCol];
            const float WorldZ = HeightToWorldCm(NormHeight);
            const FVector2D WorldXY = GridToWorldXY(GridCol, GridRow);

            Vertices.Add(FVector(WorldXY.X, WorldXY.Y, WorldZ));
            UVs.Add(FVector2D(
                (float)GridCol / GridSize,
                (float)GridRow / GridSize));
            Normals.Add(FVector::UpVector); // will be recalculated by UE
        }
    }

    // Build triangle indices (two triangles per quad)
    for (int32 Row = 0; Row < Rows - 1; ++Row)
    {
        for (int32 Col = 0; Col < Cols - 1; ++Col)
        {
            const int32 TL = Row * Cols + Col;
            const int32 TR = TL + 1;
            const int32 BL = TL + Cols;
            const int32 BR = BL + 1;

            Triangles.Add(TL);
            Triangles.Add(BL);
            Triangles.Add(TR);

            Triangles.Add(TR);
            Triangles.Add(BL);
            Triangles.Add(BR);
        }
    }

    ProcMesh->CreateMeshSection(SectionIndex, Vertices, Triangles,
        Normals, UVs, VertexColors,
        TArray<FProcMeshTangent>(),
        /*bCreateCollision=*/true);
}

// ─────────────────────────────────────────────────────────────────────────────

float AGeoTimeTerrainActor::HeightToWorldCm(float NormalizedHeight) const
{
    // HeightMap values in the backend range approximately [-1, 1].
    // Map them to [MinHeightCm, MaxHeightCm].
    const float Range  = TerrainMeta.MaxHeightCm - TerrainMeta.MinHeightCm;
    const float Offset = TerrainMeta.MinHeightCm;
    return Offset + (NormalizedHeight * 0.5f + 0.5f) * Range;
}

FVector2D AGeoTimeTerrainActor::GridToWorldXY(int32 Col, int32 Row) const
{
    return FVector2D(
        (float)Col * TerrainMeta.CellSizeCm,
        (float)Row * TerrainMeta.CellSizeCm);
}
