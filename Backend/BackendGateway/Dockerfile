# Этап сборки
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["BackendGateway/BackendGateway.csproj", "BackendGateway/"]
RUN dotnet restore "BackendGateway/BackendGateway.csproj"
COPY BackendGateway/. ./BackendGateway/
WORKDIR /src/BackendGateway
RUN dotnet publish -c Release -o /app/publish

# Этап выполнения
FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 80
ENTRYPOINT ["dotnet", "BackendGateway.dll"]
