// Copyright GeoTime Contributors. All Rights Reserved.

#include "GeoTimeBackendClient.h"
#include "HttpModule.h"
#include "Interfaces/IHttpResponse.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

UGeoTimeBackendClient::UGeoTimeBackendClient()
{
    Http = &FHttpModule::Get();
}

// ─── Private helpers ──────────────────────────────────────────────────────────

TSharedRef<IHttpRequest, ESPMode::ThreadSafe>
UGeoTimeBackendClient::MakeRequest(const FString& Verb, const FString& Path)
{
    auto Req = Http->CreateRequest();
    Req->SetVerb(Verb);
    Req->SetURL(BackendUrl + Path);
    Req->SetHeader(TEXT("Accept"), TEXT("*/*"));
    return Req;
}

FCameraState UGeoTimeBackendClient::ParseCameraState(
    const TSharedPtr<FJsonObject>& Json)
{
    FCameraState State;
    if (!Json.IsValid()) return State;
    Json->TryGetStringField(TEXT("mode"),      State.Mode);
    Json->TryGetNumberField(TEXT("lat"),        State.Lat);
    Json->TryGetNumberField(TEXT("lon"),        State.Lon);
    Json->TryGetNumberField(TEXT("altitudeKm"), State.AltitudeKm);
    Json->TryGetNumberField(TEXT("heading"),    State.Heading);
    Json->TryGetNumberField(TEXT("pitch"),      State.Pitch);
    return State;
}

// ─── FetchTerrainMeta ─────────────────────────────────────────────────────────

void UGeoTimeBackendClient::FetchTerrainMeta()
{
    auto Req = MakeRequest(TEXT("GET"), TEXT("/api/unreal/terrain-meta"));
    Req->OnProcessRequestComplete().BindUObject(
        this, &UGeoTimeBackendClient::OnTerrainMetaResponse);
    Req->ProcessRequest();
}

void UGeoTimeBackendClient::OnTerrainMetaResponse(
    FHttpRequestPtr /*Req*/, FHttpResponsePtr Res, bool bSuccess)
{
    if (!bSuccess || !Res.IsValid() || Res->GetResponseCode() != 200)
    {
        OnRequestError.ExecuteIfBound(TEXT("FetchTerrainMeta failed"));
        return;
    }

    TSharedPtr<FJsonObject> Json;
    TSharedRef<TJsonReader<>> Reader =
        TJsonReaderFactory<>::Create(Res->GetContentAsString());
    if (!FJsonSerializer::Deserialize(Reader, Json) || !Json.IsValid())
    {
        OnRequestError.ExecuteIfBound(TEXT("FetchTerrainMeta: JSON parse error"));
        return;
    }

    FTerrainMeta Meta;
    Json->TryGetNumberField(TEXT("gridSize"),                Meta.GridSize);
    Json->TryGetNumberField(TEXT("cellCount"),               Meta.CellCount);
    Json->TryGetNumberField(TEXT("cellSizeCm"),              Meta.CellSizeCm);
    Json->TryGetNumberField(TEXT("maxHeightCm"),             Meta.MaxHeightCm);
    Json->TryGetNumberField(TEXT("minHeightCm"),             Meta.MinHeightCm);
    Json->TryGetNumberField(TEXT("firstPersonThresholdKm"),  Meta.FirstPersonThresholdKm);
    OnTerrainMetaReceived.ExecuteIfBound(Meta);
}

// ─── FetchHeightmapRaw ────────────────────────────────────────────────────────

void UGeoTimeBackendClient::FetchHeightmapRaw()
{
    auto Req = MakeRequest(TEXT("GET"), TEXT("/api/unreal/heightmap-raw"));
    Req->OnProcessRequestComplete().BindUObject(
        this, &UGeoTimeBackendClient::OnHeightmapRawResponse);
    Req->ProcessRequest();
}

void UGeoTimeBackendClient::OnHeightmapRawResponse(
    FHttpRequestPtr /*Req*/, FHttpResponsePtr Res, bool bSuccess)
{
    if (!bSuccess || !Res.IsValid() || Res->GetResponseCode() != 200)
    {
        OnRequestError.ExecuteIfBound(TEXT("FetchHeightmapRaw failed"));
        return;
    }

    const TArray<uint8>& Bytes = Res->GetContent();
    const int32 NumFloats = Bytes.Num() / sizeof(float);
    TArray<float> Heights;
    Heights.SetNumUninitialized(NumFloats);
    FMemory::Memcpy(Heights.GetData(), Bytes.GetData(), NumFloats * sizeof(float));
    OnHeightmapReceived.ExecuteIfBound(Heights);
}

// ─── FetchTerrainTile ─────────────────────────────────────────────────────────

void UGeoTimeBackendClient::FetchTerrainTile(int32 TileX, int32 TileY, int32 Lod)
{
    const FString Path = FString::Printf(
        TEXT("/api/unreal/terrain-tile/%d/%d/%d"), TileX, TileY, Lod);
    auto Req = MakeRequest(TEXT("GET"), Path);
    Req->OnProcessRequestComplete().BindUObject(
        this, &UGeoTimeBackendClient::OnTerrainTileResponse);
    Req->ProcessRequest();
}

void UGeoTimeBackendClient::OnTerrainTileResponse(
    FHttpRequestPtr /*Req*/, FHttpResponsePtr Res, bool bSuccess)
{
    if (!bSuccess || !Res.IsValid() || Res->GetResponseCode() != 200)
    {
        OnRequestError.ExecuteIfBound(TEXT("FetchTerrainTile failed"));
        return;
    }

    const TArray<uint8>& Bytes = Res->GetContent();
    const int32 NumFloats = Bytes.Num() / sizeof(float);
    TArray<float> Heights;
    Heights.SetNumUninitialized(NumFloats);
    FMemory::Memcpy(Heights.GetData(), Bytes.GetData(), NumFloats * sizeof(float));
    OnTerrainTileReceived.ExecuteIfBound(Heights);
}

// ─── Camera state ─────────────────────────────────────────────────────────────

void UGeoTimeBackendClient::FetchCameraState()
{
    auto Req = MakeRequest(TEXT("GET"), TEXT("/api/unreal/camera"));
    Req->OnProcessRequestComplete().BindUObject(
        this, &UGeoTimeBackendClient::OnCameraStateResponse);
    Req->ProcessRequest();
}

void UGeoTimeBackendClient::PushCameraState(const FCameraState& State)
{
    TSharedPtr<FJsonObject> JsonObj = MakeShared<FJsonObject>();
    JsonObj->SetStringField(TEXT("mode"),       State.Mode);
    JsonObj->SetNumberField(TEXT("lat"),         State.Lat);
    JsonObj->SetNumberField(TEXT("lon"),         State.Lon);
    JsonObj->SetNumberField(TEXT("altitudeKm"),  State.AltitudeKm);
    JsonObj->SetNumberField(TEXT("heading"),     State.Heading);
    JsonObj->SetNumberField(TEXT("pitch"),       State.Pitch);

    FString Body;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Body);
    FJsonSerializer::Serialize(JsonObj.ToSharedRef(), Writer);

    auto Req = MakeRequest(TEXT("PUT"), TEXT("/api/unreal/camera"));
    Req->SetHeader(TEXT("Content-Type"), TEXT("application/json"));
    Req->SetContentAsString(Body);
    Req->OnProcessRequestComplete().BindUObject(
        this, &UGeoTimeBackendClient::OnCameraStateResponse);
    Req->ProcessRequest();
}

void UGeoTimeBackendClient::OnCameraStateResponse(
    FHttpRequestPtr /*Req*/, FHttpResponsePtr Res, bool bSuccess)
{
    if (!bSuccess || !Res.IsValid() || Res->GetResponseCode() != 200)
    {
        OnRequestError.ExecuteIfBound(TEXT("Camera state request failed"));
        return;
    }

    TSharedPtr<FJsonObject> Json;
    TSharedRef<TJsonReader<>> Reader =
        TJsonReaderFactory<>::Create(Res->GetContentAsString());
    if (!FJsonSerializer::Deserialize(Reader, Json) || !Json.IsValid())
    {
        OnRequestError.ExecuteIfBound(TEXT("Camera state: JSON parse error"));
        return;
    }

    OnCameraStateReceived.ExecuteIfBound(ParseCameraState(Json));
}
