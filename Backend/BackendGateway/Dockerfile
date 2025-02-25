# Этап сборки
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Backend/BackendGateway/BackendGateway.csproj", "Backend/BackendGateway/"]
RUN dotnet restore "Backend/BackendGateway/BackendGateway.csproj"
COPY Backend/BackendGateway/. ./Backend/BackendGateway/
WORKDIR /src/Backend/BackendGateway
RUN dotnet publish -c Release -o /app/publish

# Этап выполнения
FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 80
ENTRYPOINT ["dotnet", "BackendGateway.dll"]
