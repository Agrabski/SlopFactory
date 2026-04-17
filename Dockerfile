# Use a Debian base image as a robust foundation
FROM debian:bookworm-slim

# Set environment variables for non-interactive installation
ENV DEBIAN_FRONTEND=noninteractive

# 1. Install Git, necessary utilities, and build dependencies
# Combine all package installations into one RUN layer for better image caching and size.
RUN apt-get update && apt-get install -y \
    git \
    wget \
    unzip \
    xz-utils \
    ca-certificates \
    curl \
    && apt-get clean && rm -rf /var/lib/apt/lists/* 

ENV FLUTTER_VERSION 3.41.7
ENV FLUTTER_DIR /opt/flutter
RUN mkdir -p ${FLUTTER_DIR} && \
    wget https://storage.googleapis.com/flutter_infra_release/releases/stable/linux/flutter_linux_${FLUTTER_VERSION}-stable.tar.xz \
    && tar xf flutter_linux_${FLUTTER_VERSION}-stable.tar.xz -C ${FLUTTER_DIR} --strip-components=1 \
    && rm flutter_linux_${FLUTTER_VERSION}-stable.tar.xz

ENV PATH="${PATH}:${FLUTTER_DIR}/bin"

RUN curl -sSL https://pkgs.microsoft.com/rascore/linux-ubuntu/dde/packages/dotnet-install.sh | bash /dev/stdin --installed dotnet --version 10.0

ENV PATH="${PATH}:/usr/share/dotnet"



# 4. Set the working directory for the application
WORKDIR /app

# Example command to run tests or the application
# ENTRYPOINT ["/bin/bash"]