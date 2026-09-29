FROM mcr.microsoft.com/dotnet/runtime-deps:10.0

ARG DOTNET_SDK_VERSION

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl git nodejs npm \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --version "$DOTNET_SDK_VERSION" --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && rm /tmp/dotnet-install.sh

RUN npm install --global @anthropic-ai/claude-code

RUN useradd --create-home --shell /bin/bash sandbox \
    && mkdir -p /home/sandbox/.config \
    && chown -R sandbox:sandbox /home/sandbox

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
USER sandbox
WORKDIR /home/sandbox
