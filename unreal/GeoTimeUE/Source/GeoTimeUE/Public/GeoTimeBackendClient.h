// Copyright GeoTime Contributors. All Rights Reserved.
//
// GeoTimeBackendClient
// --------------------
// Thin HTTP wrapper that communicates with the GeoTime .NET backend REST API.
// All requests are asynchronous; results are delivered via delegates on the
// game thread so callers can safely update UObjects from their callbacks.

#pragma once

#include "CoreMinimal.h"
#include "UObject/NoExportTypes.h"
#include "Http.h"
#include "Dom/JsonObject.h"
#include "GeoTimeBackendClient.generated.h"

// ─── Struct mirrors of backend response types ────────────────────────────────

USTRUCT(BlueprintType)
struct FTerrainMeta
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category = "GeoTime|Terrain")
    int32 GridSize = 512;

    UPROPERTY(BlueprintReadOnly, Category = "GeoTime|Terrain")
    int32 CellCount = 512 * 512;

    /** Width/height of each terrain cell in Unreal units (1 UU = 1 cm). */
    UPROPERTY(BlueprintReadOnly, Category = "GeoTime|Terrain")
    float CellSizeCm = 60000.f;

    /** Maximum terrain elevation in Unreal units (cm). */
    UPROPERTY(BlueprintReadOnly, Category = "GeoTime|Terrain")
    float MaxHeightCm = 2000000.f;

    /** Minimum terrain elevation (ocean floor) in Unreal units (cm). */
    UPROPERTY(BlueprintReadOnly, Category = "GeoTime|Terrain")
    float MinHeightCm = -1100000.f;

    /**
     * Altitude threshold (km) below which the camera switches from orbit view
     * to first-person view.
     */
    UPROPERTY(BlueprintReadOnly, Category = "GeoTime|Camera")
    double FirstPersonThresholdKm = 0.1;
};

USTRUCT(BlueprintType)
struct FCameraState
{
    GENERATED_BODY()

    /** "orbit" or "firstperson". */
    UPROPERTY(BlueprintReadWrite, Category = "GeoTime|Camera")
    FString Mode = TEXT("orbit");

    UPROPERTY(BlueprintReadWrite, Category = "GeoTime|Camera")
    double Lat = 0.0;

    UPROPERTY(BlueprintReadWrite, Category = "GeoTime|Camera")
    double Lon = 0.0;

    /** Camera altitude above the terrain surface, in km. */
    UPROPERTY(BlueprintReadWrite, Category = "GeoTime|Camera")
    double AltitudeKm = 500.0;

    /** Heading (yaw) in degrees, clockwise from north. */
    UPROPERTY(BlueprintReadWrite, Category = "GeoTime|Camera")
    double Heading = 0.0;

    /** Camera pitch in degrees. Negative = look down. */
    UPROPERTY(BlueprintReadWrite, Category = "GeoTime|Camera")
    double Pitch = -45.0;
};

// ─── Delegate declarations ────────────────────────────────────────────────────

DECLARE_DELEGATE_OneParam(FOnTerrainMetaReceived,  const FTerrainMeta&);
DECLARE_DELEGATE_OneParam(FOnHeightmapReceived,    const TArray<float>&);
DECLARE_DELEGATE_OneParam(FOnTerrainTileReceived,  const TArray<float>&);
DECLARE_DELEGATE_OneParam(FOnCameraStateReceived,  const FCameraState&);
DECLARE_DELEGATE_OneParam(FOnRequestError,         const FString&);

// ─── UGeoTimeBackendClient ────────────────────────────────────────────────────

/**
 * Communicates with the GeoTime .NET backend over HTTP.
 * Create one instance per world and hold a reference to it.
 *
 * Example usage:
 *   Client->OnTerrainMetaReceived.BindUObject(this, &AMyActor::HandleTerrainMeta);
 *   Client->FetchTerrainMeta();
 */
UCLASS(BlueprintType, Blueprintable)
class GEOTIMEUE_API UGeoTimeBackendClient : public UObject
{
    GENERATED_BODY()

public:
    UGeoTimeBackendClient();

    /** Base URL of the GeoTime .NET backend (no trailing slash). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GeoTime")
    FString BackendUrl = TEXT("http://localhost:5000");

    // ── Delegates ─────────────────────────────────────────────────────────────
    FOnTerrainMetaReceived  OnTerrainMetaReceived;
    FOnHeightmapReceived    OnHeightmapReceived;
    FOnTerrainTileReceived  OnTerrainTileReceived;
    FOnCameraStateReceived  OnCameraStateReceived;
    FOnRequestError         OnRequestError;

    // ── Methods ───────────────────────────────────────────────────────────────

    /** Asynchronously fetch terrain metadata. Fires OnTerrainMetaReceived. */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Terrain")
    void FetchTerrainMeta();

    /** Asynchronously fetch the full float32 heightmap. Fires OnHeightmapReceived. */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Terrain")
    void FetchHeightmapRaw();

    /**
     * Asynchronously fetch a terrain tile at the given LOD.
     * @param TileX  Tile column index.
     * @param TileY  Tile row index.
     * @param Lod    Level-of-detail: 0 = full resolution, 1 = half, 2 = quarter.
     */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Terrain")
    void FetchTerrainTile(int32 TileX, int32 TileY, int32 Lod);

    /** Asynchronously read the current camera state. Fires OnCameraStateReceived. */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Camera")
    void FetchCameraState();

    /**
     * Push an updated camera state to the backend.
     * The server will enforce the first-person altitude threshold and
     * return the canonical state through OnCameraStateReceived.
     */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Camera")
    void PushCameraState(const FCameraState& State);

private:
    FHttpModule* Http = nullptr;

    TSharedRef<IHttpRequest, ESPMode::ThreadSafe> MakeRequest(
        const FString& Verb, const FString& Path);

    void OnTerrainMetaResponse(FHttpRequestPtr Req,
        FHttpResponsePtr Res, bool bSuccess);
    void OnHeightmapRawResponse(FHttpRequestPtr Req,
        FHttpResponsePtr Res, bool bSuccess);
    void OnTerrainTileResponse(FHttpRequestPtr Req,
        FHttpResponsePtr Res, bool bSuccess);
    void OnCameraStateResponse(FHttpRequestPtr Req,
        FHttpResponsePtr Res, bool bSuccess);

    static FCameraState ParseCameraState(const TSharedPtr<FJsonObject>& Json);
};
