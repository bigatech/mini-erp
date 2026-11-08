FROM node:22-alpine AS build
WORKDIR /app
COPY src/web/MiniErp.Web/package*.json ./
RUN npm ci
COPY src/web/MiniErp.Web/ ./
ARG VITE_API_URL=
ARG VITE_APP_ENVIRONMENT=Production
ARG VITE_OIDC_AUTHORITY=http://localhost:18090/realms/minierp-local
ARG VITE_OIDC_CLIENT_ID=minierp-web
ARG VITE_BUILD_TIME
ENV VITE_API_URL=$VITE_API_URL VITE_APP_ENVIRONMENT=$VITE_APP_ENVIRONMENT
ENV VITE_OIDC_AUTHORITY=$VITE_OIDC_AUTHORITY VITE_OIDC_CLIENT_ID=$VITE_OIDC_CLIENT_ID
RUN export VITE_BUILD_TIME="${VITE_BUILD_TIME:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}" && npm run build

FROM nginx:1.27-alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY deploy/docker/nginx.conf /etc/nginx/templates/default.conf.template
ENV API_UPSTREAM=http://api:8080
EXPOSE 80
