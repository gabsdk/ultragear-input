FROM ubuntu:24.04
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        adwaita-icon-theme-full ca-certificates dbus-daemon dbus-x11 fonts-cantarell gnome-shell \
        gnome-shell-extension-appindicator gjs i2c-tools imagemagick libglib2.0-bin librsvg2-common python3 \
        x11-apps xfce4-panel xfconf xvfb \
    && rm -rf /var/lib/apt/lists/* \
    && rm -f /usr/share/dbus-1/system-services/org.freedesktop.login1.service \
    && useradd --create-home tester
COPY entrypoint.sh /usr/local/bin/ultragear-test-entrypoint
ENTRYPOINT ["/usr/local/bin/ultragear-test-entrypoint"]
