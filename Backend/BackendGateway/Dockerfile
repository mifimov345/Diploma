# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["BackendGateway/BackendGateway.csproj", "BackendGateway/"]
RUN dotnet restore "BackendGateway/BackendGateway.csproj"

COPY . .
WORKDIR "/src/BackendGateway"
RUN dotnet publish "BackendGateway.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 80
EXPOSE 443
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BackendGateway.dll"]
