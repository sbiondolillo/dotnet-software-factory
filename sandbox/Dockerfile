FROM node:22-bookworm-slim

ARG DOTNET_SDK_VERSION

RUN apt-get update \
    && apt-get install --no-install-recommends -y git curl ca-certificates libicu72 \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --version "${DOTNET_SDK_VERSION}" --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm /tmp/dotnet-install.sh

RUN npm install -g @anthropic-ai/claude-code

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

USER node
RUN mkdir -p /home/node/.config
WORKDIR /home/node
