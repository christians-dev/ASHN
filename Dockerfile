# syntax=docker/dockerfile:1
# Ashen as one container: the React SPA is built, then served by the .NET API as static files (plan.md).
#   docker build -t ashen .
#   docker run -p 8080:8080 -e ConnectionStrings__ashen="Host=...;Database=ashen;Username=...;Password=..." ashen
# Health probe for the load balancer / ECS: GET /alive (200 when the process is up).

# 1. Build the SPA.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2. Publish the API.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /repo
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Ashen.Api/Ashen.Api.csproj -c Release -o /app

# 3. Runtime: ASP.NET only (no SDK), running as the image's non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot

# Product images are written here.
# shortcut: container disk is lost on redeploy; move images to S3 before running more than one task in production.
RUN mkdir -p /app/media && chown "$APP_UID" /app/media

# Forwarded headers: behind a load balancer that terminates TLS, trust X-Forwarded-For/-Proto so the app sees the
# real scheme (Google sign-in callback URL) and client IP (rate limiting). Only expose the container via that proxy.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    Catalog__Media__RootPath=/app/media

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ashen.Api.dll"]
