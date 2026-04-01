// Copyright GeoTime Contributors. All Rights Reserved.

#include "GeoTimeCameraManager.h"
#include "GameFramework/PlayerController.h"
#include "Kismet/KismetMathLibrary.h"

AGeoTimeCameraManager::AGeoTimeCameraManager()
{
    bAlwaysApplyModifiers = true;
}

// ─────────────────────────────────────────────────────────────────────────────

void AGeoTimeCameraManager::BeginPlay()
{
    Super::BeginPlay();
}

// ─────────────────────────────────────────────────────────────────────────────

void AGeoTimeCameraManager::Initialize(UGeoTimeBackendClient* Client,
                                        AGeoTimeTerrainActor*  Terrain,
                                        const FTerrainMeta&    Meta)
{
    BackendClient = Client;
    TerrainActor  = Terrain;
    CachedMeta    = Meta;

    // Override the threshold from the backend if it supplies one.
    if (Meta.FirstPersonThresholdKm > 0.0)
        FirstPersonThresholdKm = Meta.FirstPersonThresholdKm;

    // Fetch the current server-side camera state so we start in the
    // position that was last saved (e.g. after a reconnect).
    if (BackendClient)
    {
        BackendClient->OnCameraStateReceived.BindLambda(
            [this](const FCameraState& State)
            {
                FocusLat        = State.Lat;
                FocusLon        = State.Lon;
                OrbitAltitudeKm = State.AltitudeKm;
                OrbitHeading    = State.Heading;
                OrbitPitch      = State.Pitch;

                if (State.Mode == TEXT("firstperson"))
                    TransitionToFirstPerson();
            });
        BackendClient->FetchCameraState();
    }
}

// ─── Zoom ─────────────────────────────────────────────────────────────────────

void AGeoTimeCameraManager::ZoomIn()
{
    if (CameraMode == EGeoTimeCameraMode::FirstPerson)
    {
        // Already in first-person; no further zoom makes sense.
        return;
    }

    OrbitAltitudeKm = FMath::Max(
        MinOrbitAltitudeKm,
        OrbitAltitudeKm - ZoomSpeedKm);

    // Clamp the zoom speed progressively as we get close to the ground,
    // so the transition feels smooth rather than sudden.
    if (OrbitAltitudeKm > 1.0)
        ZoomSpeedKm = FMath::Max(1.0, OrbitAltitudeKm * 0.2);
    else
        ZoomSpeedKm = 0.05; // fine-grained control near the surface

    // Check whether we have crossed the first-person threshold.
    if (OrbitAltitudeKm < FirstPersonThresholdKm)
    {
        TransitionToFirstPerson();
    }
    else
    {
        SyncCameraStateToBackend();
    }
}

void AGeoTimeCameraManager::ZoomOut()
{
    if (CameraMode == EGeoTimeCameraMode::FirstPerson)
    {
        // Rising back up: leave first-person mode.
        TransitionToOrbit();
        return;
    }

    OrbitAltitudeKm += ZoomSpeedKm;

    // Restore normal zoom speed when the altitude rises above 1 km.
    if (OrbitAltitudeKm > 1.0)
        ZoomSpeedKm = FMath::Max(50.0, OrbitAltitudeKm * 0.2);

    SyncCameraStateToBackend();
}

// ─── Look / pan ───────────────────────────────────────────────────────────────

void AGeoTimeCameraManager::AddYawInput(float Delta)
{
    if (CameraMode == EGeoTimeCameraMode::Orbit)
    {
        OrbitHeading = FMath::Fmod(OrbitHeading + Delta, 360.0);
    }
    else
    {
        FPSHeading = FMath::Fmod(FPSHeading + Delta, 360.0);
    }
}

void AGeoTimeCameraManager::AddPitchInput(float Delta)
{
    if (CameraMode == EGeoTimeCameraMode::Orbit)
    {
        OrbitPitch = FMath::Clamp(OrbitPitch + Delta, -89.0, -5.0);
    }
    else
    {
        FPSPitch = FMath::Clamp(FPSPitch + Delta, -89.0, 89.0);
    }
}

// ─── Mode transitions ─────────────────────────────────────────────────────────

void AGeoTimeCameraManager::TransitionToFirstPerson()
{
    if (CameraMode == EGeoTimeCameraMode::FirstPerson) return;

    CameraMode  = EGeoTimeCameraMode::FirstPerson;
    FPSLat      = FocusLat;
    FPSLon      = FocusLon;
    FPSHeading  = OrbitHeading;
    FPSPitch    = 0.0;

    // When entering first-person set the altitude to ground level so the
    // backend records the transition correctly.
    OrbitAltitudeKm = 0.0;

    SyncCameraStateToBackend();

    UE_LOG(LogTemp, Log,
        TEXT("GeoTimeCameraManager: → First-Person at (%.4f, %.4f)"),
        FPSLat, FPSLon);
}

void AGeoTimeCameraManager::TransitionToOrbit()
{
    if (CameraMode == EGeoTimeCameraMode::Orbit) return;

    CameraMode = EGeoTimeCameraMode::Orbit;

    // Rise to just above the first-person threshold so we do not
    // immediately fall back into it.
    OrbitAltitudeKm = FirstPersonThresholdKm * 2.0;
    FocusLat        = FPSLat;
    FocusLon        = FPSLon;
    ZoomSpeedKm     = 0.05; // Start slow when emerging from the ground

    SyncCameraStateToBackend();

    UE_LOG(LogTemp, Log,
        TEXT("GeoTimeCameraManager: → Orbit at altitude %.4f km"),
        OrbitAltitudeKm);
}

// ─── UpdateCamera ─────────────────────────────────────────────────────────────

void AGeoTimeCameraManager::UpdateCamera(float DeltaTime)
{
    Super::UpdateCamera(DeltaTime);

    FTransform CamTransform = (CameraMode == EGeoTimeCameraMode::Orbit)
        ? ComputeOrbitTransform()
        : ComputeFirstPersonTransform();

    SetActorLocationAndRotation(
        CamTransform.GetLocation(),
        CamTransform.GetRotation());
}

// ─── Transform helpers ────────────────────────────────────────────────────────

FTransform AGeoTimeCameraManager::ComputeOrbitTransform() const
{
    // Position the camera directly above the focus point at OrbitAltitudeKm,
    // tilted by OrbitPitch and rotated by OrbitHeading.
    const FVector FocusWorld  = LatLonToWorldPos(FocusLat, FocusLon);
    const double AltitudeCm   = OrbitAltitudeKm * 100'000.0; // km → cm

    // Start with the camera straight above, then apply heading and pitch.
    const FRotator CamRot =
        FRotator(OrbitPitch, OrbitHeading, 0.0);

    // Offset backward along the camera's forward direction by the altitude.
    const FVector Offset =
        CamRot.RotateVector(FVector(-AltitudeCm, 0.0, 0.0));

    return FTransform(CamRot, FocusWorld + FVector(0, 0, AltitudeCm) + Offset);
}

FTransform AGeoTimeCameraManager::ComputeFirstPersonTransform() const
{
    // Position the camera at eye level on the terrain surface.
    const double EyeHeightCm = 180.0; // 1.8 m

    FVector GroundPos = LatLonToWorldPos(FPSLat, FPSLon);
    GroundPos.Z += static_cast<float>(EyeHeightCm);

    const FRotator CamRot = FRotator(FPSPitch, FPSHeading, 0.0);

    return FTransform(CamRot, GroundPos);
}

FVector AGeoTimeCameraManager::LatLonToWorldPos(double Lat, double Lon) const
{
    if (!TerrainActor) return FVector::ZeroVector;

    const int32 GridSize    = CachedMeta.GridSize;
    const float CellSizeCm  = CachedMeta.CellSizeCm;

    const double NormLon = (Lon + 180.0) / 360.0;
    const double NormLat = (90.0 - Lat)  / 180.0;

    const float WorldX   = static_cast<float>(NormLon * GridSize * CellSizeCm);
    const float WorldY   = static_cast<float>(NormLat * GridSize * CellSizeCm);
    const float WorldZ   = TerrainActor->SampleHeightAtLatLon(Lat, Lon);

    return FVector(WorldX, WorldY, WorldZ);
}

// ─── Backend sync ─────────────────────────────────────────────────────────────

void AGeoTimeCameraManager::SyncCameraStateToBackend() const
{
    if (!BackendClient) return;

    FCameraState State;
    State.Mode       = (CameraMode == EGeoTimeCameraMode::FirstPerson)
                         ? TEXT("firstperson")
                         : TEXT("orbit");
    State.Lat        = FocusLat;
    State.Lon        = FocusLon;
    State.AltitudeKm = OrbitAltitudeKm;
    State.Heading    = OrbitHeading;
    State.Pitch      = OrbitPitch;
    BackendClient->PushCameraState(State);
}
