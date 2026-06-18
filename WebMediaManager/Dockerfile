# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
# Create the SQLite data directory owned by the non-root app user. A fresh named volume mounted at
# /data inherits this ownership, so the app can create wmm.sqlite. Without this the volume mounts as
# root and SQLite fails with "error 14: unable to open database file".
RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
USER $APP_UID


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["WebMediaManager/WebMediaManager.csproj", "WebMediaManager/"]
RUN dotnet restore "./WebMediaManager/WebMediaManager.csproj"
COPY . .
WORKDIR "/src/WebMediaManager"
RUN dotnet build "./WebMediaManager.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./WebMediaManager.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WebMediaManager.dll"]