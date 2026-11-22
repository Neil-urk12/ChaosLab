# Build from the repository root (make build): the image holds the backend and the built frontend.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
# ASP.NET's default Data Protection key folder (login cookies, refresh tokens). backend-pod.yaml mounts a volume here;
# creating it as the app user lets a new, empty volume take this owner. The same goes for the reports volume.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys /home/app/reports
WORKDIR /app
# The image serves the web app itself, so the first-run setup link points here.
ENV BootstrapSetup__PublicUrl=http://localhost:8080
EXPOSE 8080
EXPOSE 8081

FROM node:24-alpine AS frontend
RUN npm install -g pnpm@12
WORKDIR /src
COPY ["Frontend/package.json", "Frontend/pnpm-lock.yaml", "Frontend/pnpm-workspace.yaml", "./"]
RUN pnpm install --frozen-lockfile
COPY Frontend/ .
RUN pnpm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/Backend.csproj", "./"]
RUN dotnet restore "Backend.csproj"
COPY Backend/ .
WORKDIR "/src/"
RUN dotnet build "./Backend.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Backend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=frontend /src/build ./wwwroot
ENTRYPOINT ["dotnet", "Backend.dll"]
