FROM node:26.13.1-alpine AS build
WORKDIR /src

ARG APP_FILTER
ARG APP_DIR
COPY frontend/package.json frontend/pnpm-lock.yaml frontend/pnpm-workspace.yaml ./frontend/
RUN npm install --global "$(node -p 'require("./frontend/package.json").packageManager')"
COPY frontend ./frontend
RUN pnpm -C frontend install --frozen-lockfile
RUN NODE_OPTIONS=--max-old-space-size=4096 pnpm -C frontend --filter "$APP_FILTER" build

FROM nginx:1.27-alpine AS final
ARG APP_DIR
RUN addgroup -S appgroup && adduser -S -D -H -G appgroup appuser
RUN sed -i 's|pid.*nginx.pid;|pid /tmp/nginx.pid;|' /etc/nginx/nginx.conf
COPY infra/docker/nginx-spa.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/$APP_DIR/dist /usr/share/nginx/html
RUN chown -R appuser:appgroup /etc/nginx/conf.d /usr/share/nginx/html /var/cache/nginx /var/log/nginx
USER appuser
