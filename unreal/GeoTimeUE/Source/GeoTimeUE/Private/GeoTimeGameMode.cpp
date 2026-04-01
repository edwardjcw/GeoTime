// Copyright GeoTime Contributors. All Rights Reserved.

#include "GeoTimeGameMode.h"
#include "GeoTimeCameraManager.h"
#include "Kismet/GameplayStatics.h"

AGeoTimeGameMode::AGeoTimeGameMode()
{
    // Use our camera manager by default.
    PlayerCameraManagerClass = AGeoTimeCameraManager::StaticClass();
}

// ─────────────────────────────────────────────────────────────────────────────

void AGeoTimeGameMode::BeginPlay()
{
    Super::BeginPlay();

    // Create the backend HTTP client.
    BackendClient = NewObject<UGeoTimeBackendClient>(this);
    BackendClient->BackendUrl = BackendUrl;

    // Wire up callbacks.
    BackendClient->OnTerrainMetaReceived.BindUObject(
        this, &AGeoTimeGameMode::OnTerrainMetaReceived);
    BackendClient->OnRequestError.BindUObject(
        this, &AGeoTimeGameMode::OnBackendError);

    // Kick off terrain metadata fetch – everything else follows from that.
    BackendClient->FetchTerrainMeta();

    UE_LOG(LogTemp, Log,
        TEXT("GeoTimeGameMode: connecting to backend at %s"), *BackendUrl);
}

// ─────────────────────────────────────────────────────────────────────────────

void AGeoTimeGameMode::OnTerrainMetaReceived(const FTerrainMeta& Meta)
{
    TerrainMeta = Meta;

    UE_LOG(LogTemp, Log,
        TEXT("GeoTimeGameMode: terrain meta – grid %d, cellSize %.0f cm"),
        Meta.GridSize, Meta.CellSizeCm);

    FinishSetup();
}

void AGeoTimeGameMode::OnBackendError(const FString& ErrorMessage)
{
    UE_LOG(LogTemp, Error,
        TEXT("GeoTimeGameMode: backend error – %s"), *ErrorMessage);
}

// ─────────────────────────────────────────────────────────────────────────────

void AGeoTimeGameMode::FinishSetup()
{
    // Spawn the terrain actor in the world.
    UWorld* World = GetWorld();
    if (!World) return;

    TSubclassOf<AGeoTimeTerrainActor> ActorClass =
        TerrainActorClass ? TerrainActorClass : AGeoTimeTerrainActor::StaticClass();

    FActorSpawnParameters Params;
    Params.Name = TEXT("GeoTimeTerrain");
    TerrainActor = World->SpawnActor<AGeoTimeTerrainActor>(
        ActorClass, FVector::ZeroVector, FRotator::ZeroRotator, Params);

    if (!TerrainActor)
    {
        UE_LOG(LogTemp, Error,
            TEXT("GeoTimeGameMode: failed to spawn TerrainActor"));
        return;
    }

    TerrainActor->Initialize(BackendClient, TerrainMeta);
    TerrainActor->LoadHeightmap();

    // Initialise the camera manager for the first local player.
    APlayerController* PC = UGameplayStatics::GetPlayerController(this, 0);
    if (PC)
    {
        AGeoTimeCameraManager* CamMgr =
            Cast<AGeoTimeCameraManager>(PC->PlayerCameraManager);
        if (CamMgr)
        {
            CamMgr->Initialize(BackendClient, TerrainActor, TerrainMeta);
        }
    }

    UE_LOG(LogTemp, Log,
        TEXT("GeoTimeGameMode: setup complete – terrain and camera ready"));
}
