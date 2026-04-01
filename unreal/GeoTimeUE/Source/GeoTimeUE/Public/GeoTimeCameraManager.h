// Copyright GeoTime Contributors. All Rights Reserved.
//
// AGeoTimeCameraManager
// ----------------------
// Manages two camera modes for the GeoTime Unreal Engine viewer:
//
//   ORBIT (globe view)
//   ──────────────────
//   The camera circles a configurable focus point on the terrain at a given
//   altitude.  Scroll-wheel input (or gamepad triggers) changes the altitude.
//   The camera always looks toward the focus point.
//
//   FIRST-PERSON (landscape view)
//   ──────────────────────────────
//   When the orbit altitude drops below FirstPersonThresholdKm (default 0.1 km
//   = 100 m), the camera transitions to a first-person actor placed on the
//   terrain surface.  The player can look around freely and walk on the
//   landscape.
//
// Usage
// ─────
//   1. Set the DefaultPawnClass to BP_GeoTimeOrbitPawn (or your own pawn)
//      and PlayerCameraManagerClass to AGeoTimeCameraManager in the
//      GameMode blueprint.
//   2. Call Initialize(Client, Terrain, Meta) from BeginPlay.
//   3. Zoom with ZoomIn() / ZoomOut(), or bind scroll wheel to them.
//
// The camera manager notifies the backend of every camera-state change so
// that the server always holds the canonical view position.

#pragma once

#include "CoreMinimal.h"
#include "Camera/PlayerCameraManager.h"
#include "GeoTimeBackendClient.h"
#include "GeoTimeTerrainActor.h"
#include "GeoTimeCameraManager.generated.h"

/** The two view modes available in the GeoTime viewer. */
UENUM(BlueprintType)
enum class EGeoTimeCameraMode : uint8
{
    Orbit       UMETA(DisplayName = "Orbit (Globe View)"),
    FirstPerson UMETA(DisplayName = "First Person (Landscape View)"),
};

UCLASS(BlueprintType, Blueprintable)
class GEOTIMEUE_API AGeoTimeCameraManager : public APlayerCameraManager
{
    GENERATED_BODY()

public:
    AGeoTimeCameraManager();

    // ── Configuration ─────────────────────────────────────────────────────────

    /** The altitude (km) below which the camera switches to first-person. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GeoTime|Camera")
    double FirstPersonThresholdKm = 0.1;

    /** Initial altitude in orbit mode (km). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GeoTime|Camera")
    double OrbitAltitudeKm = 500.0;

    /** Zoom speed multiplier (km removed/added per scroll tick). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GeoTime|Camera")
    double ZoomSpeedKm = 50.0;

    /** Minimum orbit altitude before forcing first-person transition (km). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GeoTime|Camera")
    double MinOrbitAltitudeKm = 0.0;

    /** Latitude of the current camera focus point (degrees). */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "GeoTime|Camera")
    double FocusLat = 0.0;

    /** Longitude of the current camera focus point (degrees). */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "GeoTime|Camera")
    double FocusLon = 0.0;

    /** The current camera mode. */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "GeoTime|Camera")
    EGeoTimeCameraMode CameraMode = EGeoTimeCameraMode::Orbit;

    // ── Initialisation ────────────────────────────────────────────────────────

    /**
     * Provide the camera manager with references it needs at runtime.
     * Call this from your GameMode or Pawn BeginPlay.
     */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Camera")
    void Initialize(UGeoTimeBackendClient* Client,
                    AGeoTimeTerrainActor*  Terrain,
                    const FTerrainMeta&    Meta);

    // ── Zoom / movement ───────────────────────────────────────────────────────

    /**
     * Zoom in by one step.  If the resulting altitude crosses the first-person
     * threshold the camera transitions to first-person mode automatically.
     */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Camera")
    void ZoomIn();

    /**
     * Zoom out by one step.  If the camera was in first-person mode it will
     * transition back to orbit mode.
     */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Camera")
    void ZoomOut();

    /**
     * In orbit mode: pan the focus point.
     * In first-person mode: move the player pawn.
     */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Camera")
    void AddYawInput(float Delta);

    UFUNCTION(BlueprintCallable, Category = "GeoTime|Camera")
    void AddPitchInput(float Delta);

    // ── Camera mode transitions ───────────────────────────────────────────────

    /** Explicitly transition to first-person mode at the current focus lat/lon. */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Camera")
    void TransitionToFirstPerson();

    /** Explicitly transition back to orbit mode. */
    UFUNCTION(BlueprintCallable, Category = "GeoTime|Camera")
    void TransitionToOrbit();

protected:
    virtual void BeginPlay() override;
    virtual void UpdateCamera(float DeltaTime) override;

private:
    // ── Internal state ────────────────────────────────────────────────────────

    UPROPERTY()
    UGeoTimeBackendClient* BackendClient = nullptr;

    UPROPERTY()
    AGeoTimeTerrainActor* TerrainActor = nullptr;

    FTerrainMeta CachedMeta;

    // Orbit state
    double OrbitPitch   = -45.0; // degrees; negative = looking down
    double OrbitHeading = 0.0;   // degrees; clockwise from north

    // First-person state
    double FPSPitch   = 0.0;
    double FPSHeading = 0.0;
    double FPSLat     = 0.0;
    double FPSLon     = 0.0;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /** Apply OrbitAltitudeKm and focus to build the orbit camera transform. */
    FTransform ComputeOrbitTransform() const;

    /** Build the FPS camera transform from FPSLat/FPSLon/FPSHeading/FPSPitch. */
    FTransform ComputeFirstPersonTransform() const;

    /** Notify the backend of the current camera state. */
    void SyncCameraStateToBackend() const;

    /**
     * Convert lat/lon to an Unreal world-space position on the terrain.
     * Returns FVector::ZeroVector if the terrain actor is not set.
     */
    FVector LatLonToWorldPos(double Lat, double Lon) const;
};
