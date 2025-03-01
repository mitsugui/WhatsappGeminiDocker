FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5160

ENV ASPNETCORE_URLS=http://+:5160

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["WhatsappGeminiDocker.csproj", "./"]
RUN dotnet restore "WhatsappGeminiDocker.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "WhatsappGeminiDocker.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "WhatsappGeminiDocker.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WhatsappGeminiDocker.dll"]
