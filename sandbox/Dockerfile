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

# The public API printer of a reframe run, published on the image's own runtime.
# A run lists the public API of the code under src/ with
# `dotnet /opt/api-printer/ApiPrinter.dll src`.
COPY sandbox/ApiPrinter /tmp/api-printer
RUN cd /tmp/api-printer \
    && dotnet publish -c Release -o /opt/api-printer \
    && chmod -R a+rX /opt/api-printer \
    && rm -rf /tmp/api-printer /root/.nuget /root/.local/share/NuGet

USER node
RUN mkdir -p /home/node/.config
WORKDIR /home/node
