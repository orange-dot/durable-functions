# Build stage
FROM node:20-alpine AS builder

WORKDIR /app

# Copy package files
COPY package*.json ./

# Install dependencies
RUN npm ci

# Copy source
COPY . .

# Build
RUN npm run build

# Production stage
FROM nginx:alpine

# Copy built assets
COPY --from=builder /app/dist /usr/share/nginx/html

# Copy nginx config template and startup hook. The official nginx entrypoint
# renders templates and runs docker-entrypoint.d scripts before nginx starts.
COPY nginx.conf /etc/nginx/templates/default.conf.template
COPY docker-entrypoint.d/10-generate-basic-auth.sh /docker-entrypoint.d/10-generate-basic-auth.sh

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
