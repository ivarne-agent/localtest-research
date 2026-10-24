#!/usr/bin/env bash
# Records which external names and addresses the localtest stack and the host processes (apps, Chromium, studioctl)
# talk to, for the egress review (localtest/reviews/2026-10-23-egress-domains.md). Nothing is blocked.
#
#   sudo localtest/tools/egress/capture.sh start [DIR]   # DIR defaults to /var/tmp/egress/<timestamp>
#   sudo localtest/tools/egress/capture.sh stop
#   localtest/tools/egress/summarize.sh DIR [OUT]
#
# What runs between start and stop:
#   bridge.pcap  tcpdump on the stack's Podman bridge (read from /etc/containers/networks/$NETWORK.json; started as soon
#                as the network exists, so a capture started before `env up` sees the containers' start-up): every DNS query to Aardvark, and the first packets (SYN, TLS
#                handshake, a plain-HTTP request) of every connection that leaves the bridge's subnet. Traffic between
#                containers and to the front door through the gateway stays inside the subnet and is not recorded.
#   uplink.pcap  tcpdump on the interface of the default route: DNS to the host's resolver (the host's own queries
#                and the ones Aardvark forwards for containers), SYNs, TLS ClientHellos (SNI) and plain-HTTP requests.
#   ss.tsv       a poll of `ss -tunp` every $SS_INTERVAL seconds (default 0.5): the first and the last time each
#                (process, peer) pair was seen, so host connections are attributed to a process. A connection shorter
#                than the interval can be missed; the pcaps still have it, without the process.
#   containers.tsv  every (IP, container) pair seen on the network, polled every 10 s, so a container that restarts
#                with a new address is still named.
# Snap length is 512 bytes and only connection starts are kept, so a day of capture stays in megabytes.
set -euo pipefail

CTR=${CTR:-podman}
NETWORK=${NETWORK:-localtest-v2_network}
SS_INTERVAL=${SS_INTERVAL:-0.5}
STATE_DIR=/run/egress-capture

die() {
  echo "capture.sh: $*" >&2
  exit 1
}

[[ $(id -u) -eq 0 ]] || die "run as root (sudo): tcpdump and ss -p need it"
command -v tcpdump >/dev/null || die "tcpdump is not installed"
command -v jq >/dev/null || die "jq is not installed"

# The first packets of a connection: a SYN without ACK, a TLS handshake record (the ClientHello carries the SNI), a
# plain-HTTP request with its Host header. UDP other than DNS (QUIC from Chromium, NTP) and all IPv6 are kept whole.
SYN='(tcp[tcpflags] & (tcp-syn|tcp-ack) = tcp-syn)'
TLS_HANDSHAKE='(tcp[((tcp[12:1] & 0xf0) >> 2):1] = 0x16)'
HTTP_REQUEST='(tcp dst port 80 and (tcp[tcpflags] & tcp-push != 0))'
STARTS="($SYN or $TLS_HANDSHAKE or $HTTP_REQUEST or (udp and not port 53) or ip6)"

# Starts tcpdump on the stack's bridge once the network exists, so a capture started before `env up` sees the
# containers' start-up too.
start_bridge() {
  local dir=$1 netfile=/etc/containers/networks/$NETWORK.json bridge subnet i
  for ((i = 0; i < 1200; i++)); do
    [[ -r $netfile || -e $STATE_DIR/stop ]] && break
    sleep 0.5
  done
  [[ -r $netfile ]] || return 0
  bridge=$(jq -r .network_interface "$netfile")
  subnet=$(jq -r '.subnets[0].subnet' "$netfile")
  for ((i = 0; i < 120; i++)); do
    ip link show "$bridge" >/dev/null 2>&1 && break
    sleep 0.25
  done
  printf 'bridge\t%s\nsubnet\t%s\nbridge-started\t%s\n' "$bridge" "$subnet" "$(date '+%Y-%m-%dT%H:%M:%S%z')" >>"$dir/capture.info"
  # DNS on the bridge is always kept (Aardvark answers at the gateway, inside the subnet); everything else only when
  # it leaves the subnet.
  tcpdump -i "$bridge" -s 512 -U -Z root -w "$dir/bridge.pcap" \
    "port 53 or (not (src net $subnet and dst net $subnet) and $STARTS)" 2>"$dir/tcpdump-bridge.log" &
  echo $! >"$STATE_DIR/tcpdump-bridge.pid"
  wait
}

start() {
  local dir=${1:-/var/tmp/egress/$(date +%Y%m%dT%H%M%S)}
  [[ ! -e $STATE_DIR/dir ]] || die "a capture is running into $(cat "$STATE_DIR/dir"); stop it first"
  local uplink
  uplink=$(ip -o route get 1.1.1.1 | awk '{ for (i = 1; i < NF; i++) if ($i == "dev") print $(i + 1) }')
  [[ -n $uplink ]] || die "could not read the uplink"

  mkdir -p "$dir" "$STATE_DIR"
  rm -f "$STATE_DIR/stop"
  {
    echo "started	$(date '+%Y-%m-%dT%H:%M:%S%z')"
    echo "network	$NETWORK"
    echo "uplink	$uplink"
    echo "resolvers	$(awk '$1 == "nameserver" { printf "%s ", $2 }' /etc/resolv.conf)"
  } >"$dir/capture.info"
  cp /etc/hosts "$dir/etc-hosts.txt"

  start_bridge "$dir" &
  echo $! >"$STATE_DIR/bridge-waiter.pid"
  tcpdump -i "$uplink" -s 512 -U -Z root -w "$dir/uplink.pcap" "port 53 or $STARTS" 2>"$dir/tcpdump-uplink.log" &
  echo $! >"$STATE_DIR/tcpdump-uplink.pid"

  # (process, peer) pairs; loopback is the front door, the OTLP collector and the apps themselves.
  # The pollers run without errexit: one failed ss or podman call (a probe container gone between ps and inspect)
  # must not end the loop.
  (
    set +e +o pipefail
    printf 'first\tlast\tproto\tpeer\tprocess\n' >"$dir/ss.tsv.header"
    while [[ ! -e $STATE_DIR/stop ]]; do
      ss -Htunp 2>/dev/null | sed "s/^/$(date +%s.%N) /"
      sleep "$SS_INTERVAL"
    done | awk -v out="$dir/ss.tsv" '
      {
        peer = $7
        if (peer ~ /^(127\.|\[::1\]|\[::ffff:127\.)/) next
        proc = "-"
        if (match($0, /users:\(\("[^"]+",pid=[0-9]+/)) {
          proc = substr($0, RSTART + 9, RLENGTH - 9)
          sub(/",pid=/, "/", proc)
        }
        key = $2 "\t" peer "\t" proc
        if (!(key in first)) { first[key] = $1; order[++n] = key }
        last[key] = $1
      }
      NR % 200 == 0 {
        printf "" >out
        for (i = 1; i <= n; i++) printf "%s\t%s\t%s\n", first[order[i]], last[order[i]], order[i] >>out
        close(out)
      }
      END {
        printf "" >out
        for (i = 1; i <= n; i++) printf "%s\t%s\t%s\n", first[order[i]], last[order[i]], order[i] >>out
        close(out)
      }'
  ) &
  echo $! >"$STATE_DIR/ss.pid"

  (
    set +e +o pipefail
    while [[ ! -e $STATE_DIR/stop ]]; do
      "$CTR" ps --format '{{.Names}}' 2>/dev/null | while read -r name; do
        "$CTR" inspect --format "{{range .NetworkSettings.Networks}}{{.IPAddress}}	$name{{\"\\n\"}}{{end}}" "$name" 2>/dev/null
      done
      sleep 10
    done | awk -v out="$dir/containers.tsv" 'NF == 2 && !seen[$0]++ { print >>out; fflush(out) }'
  ) &
  echo $! >"$STATE_DIR/containers.pid"

  echo "$dir" >"$STATE_DIR/dir"
  echo "Capturing into $dir (uplink $uplink; the bridge of $NETWORK as soon as it exists). Stop with: sudo $0 stop"
}

# Stops tcpdump by its pid; it flushes the file on TERM.
stop_tcpdump() {
  local pidfile=$STATE_DIR/$1.pid
  [[ -r $pidfile ]] || return 0
  kill -TERM "$(cat "$pidfile")" 2>/dev/null || true
  rm -f "$pidfile"
}

# Waits for a poller to leave its loop after the stop file appeared, so its awk sees the end of input and writes the
# final table.
wait_poller() {
  local pidfile=$STATE_DIR/$1.pid pid i
  [[ -r $pidfile ]] || return 0
  pid=$(cat "$pidfile")
  for ((i = 0; i < 30; i++)); do
    kill -0 "$pid" 2>/dev/null || break
    sleep 1
  done
  rm -f "$pidfile"
}

stop() {
  [[ -r $STATE_DIR/dir ]] || die "no capture is running"
  local dir
  dir=$(cat "$STATE_DIR/dir")
  touch "$STATE_DIR/stop"
  wait_poller ss
  wait_poller containers
  # A last snapshot of the addresses, in case a poll was missed.
  "$CTR" ps --format '{{.Names}}' 2>/dev/null | while read -r name; do
    "$CTR" inspect --format "{{range .NetworkSettings.Networks}}{{.IPAddress}}	$name{{\"\\n\"}}{{end}}" "$name" 2>/dev/null || true
  done | awk 'NF == 2' >>"$dir/containers.tsv" || true
  stop_tcpdump tcpdump-bridge
  stop_tcpdump tcpdump-uplink
  wait_poller bridge-waiter
  rm -f "$STATE_DIR/stop"
  sleep 1
  if [[ -f $dir/ss.tsv.header ]]; then
    cat "$dir/ss.tsv.header" "$dir/ss.tsv" 2>/dev/null >"$dir/ss.tsv.tmp" || true
    mv "$dir/ss.tsv.tmp" "$dir/ss.tsv"
    rm -f "$dir/ss.tsv.header"
  fi
  echo "stopped	$(date '+%Y-%m-%dT%H:%M:%S%z')" >>"$dir/capture.info"
  if [[ -n ${SUDO_UID:-} ]]; then
    chown -R "$SUDO_UID:${SUDO_GID:-$SUDO_UID}" "$dir"
  fi
  rm -f "$STATE_DIR/dir"
  echo "Stopped. Summarize with: localtest/tools/egress/summarize.sh $dir"
}

case ${1:-} in
start) start "${2:-}" ;;
stop) stop ;;
*) die "usage: capture.sh start [DIR] | stop" ;;
esac
