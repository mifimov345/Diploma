FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["FileService/FileService.csproj", "FileService/"]
RUN dotnet restore "FileService/FileService.csproj"
COPY FileService/. ./FileService/
WORKDIR /src/FileService
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 80
ENTRYPOINT ["dotnet", "FileService.dll"]
