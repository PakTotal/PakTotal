FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish PakTotal/PakTotal.csproj -c Release -o /app -p:CUE4PARSE_SKIP_NATIVE=true \
    && mkdir -p /app/modules \
    && cp -r artifacts/modules/. /app/modules/
RUN PakTotal__NativeDirectory=/app/native dotnet /app/PakTotal.dll --download-natives

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /tmp/paktotal /app/data && chown "$APP_UID" /tmp/paktotal /app/data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    PakTotal__TempDirectory=/tmp/paktotal \
    PakTotal__ModulesDirectory=/app/modules \
    PakTotal__NativeDirectory=/app/native \
    PakTotal__DatabasePath=/app/data/paktotal.db
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "PakTotal.dll"]
