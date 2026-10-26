FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Eldf.Api/Eldf.Api.csproj Eldf.Api/
RUN dotnet restore Eldf.Api/Eldf.Api.csproj
COPY . .
RUN dotnet publish Eldf.Api/Eldf.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /data
ENV ELDF_URLS=http://0.0.0.0:8001 \
    ELDF_DATA_DIR=/data \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8001
ENTRYPOINT ["dotnet", "Eldf.Api.dll"]
