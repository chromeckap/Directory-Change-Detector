FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DirectoryChangeDetector/DirectoryChangeDetector.csproj", "DirectoryChangeDetector/"]
RUN dotnet restore "DirectoryChangeDetector/DirectoryChangeDetector.csproj"
COPY . .
WORKDIR "/src/DirectoryChangeDetector"
RUN dotnet build "./DirectoryChangeDetector.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DirectoryChangeDetector.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DirectoryChangeDetector.dll"]
