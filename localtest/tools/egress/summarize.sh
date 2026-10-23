#!/usr/bin/env bash
# Turns a capture.sh directory into small text tables for the egress review.
#
#   localtest/tools/egress/summarize.sh DIR [OUT]     # OUT defaults to DIR/summary
#
# Needs tshark (apt-get install tshark). Writes to OUT:
#   dns-containers.tsv   container, name, type, count: the DNS queries each container sent to Aardvark. Stack aliases
#                        (single-label names, *.dns.podman) are left out; Aardvark answers those itself.
#   dns-host.tsv         name, type, count, asked-by-a-container (yes/no): the queries that reached the host's resolver,
#                        both the host's own and the ones Aardvark forwarded.
#   out-containers.tsv   container, destination, port, names, count: connections that left the bridge's subnet. Names
#                        are the TLS SNI or HTTP Host of the connection where one was seen, otherwise the DNS names that
#                        answered with that address during the capture.
#   out-host.tsv         destination, port, names, count: the same on the uplink (includes the containers' traffic after
#                        NAT; compare with out-containers.tsv).
#   processes.tsv        process, peer, names, first, last: the host connections from ss.tsv with the process.
#   unnamed.tsv          destination, port, count: uplink connections with no SNI, no Host and no DNS answer for the
#                        address: an address in the code or configuration, or a name resolved before the capture.
# Addresses in private ranges (the bridge, the host, the resolver) are left out of out-*.tsv and processes.tsv except
# where EGRESS_KEEP_PRIVATE=1.
set -euo pipefail

dir=${1:?usage: summarize.sh DIR [OUT]}
out=${2:-$dir/summary}
command -v tshark >/dev/null || {
  echo "summarize.sh: tshark is not installed (sudo apt-get install tshark)" >&2
  exit 1
}
mkdir -p "$out"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

subnet=$(awk -F'\t' '$1 == "subnet" { print $2 }' "$dir/capture.info")
keep_private=${EGRESS_KEEP_PRIVATE:-0}

fields() { # fields PCAP FILTER FIELD... : tab-separated, one row per packet, multiple values joined by commas.
  local pcap=$1 filter=$2
  shift 2
  local args=()
  for f in "$@"; do args+=(-e "$f"); done
  [[ -s $pcap ]] || return 0
  tshark -r "$pcap" -n -Y "$filter" -T fields -E separator=/t -E occurrence=a -E aggregator=, "${args[@]}" 2>/dev/null || true
}

# Private and special addresses: the stack, the host, the sandbox's resolver, link-local, multicast.
private_awk='function private(ip) {
  return ip ~ /^(10\.|127\.|169\.254\.|192\.168\.|0\.|22[4-9]\.|23[0-9]\.|255\.)/ ||
         ip ~ /^172\.(1[6-9]|2[0-9]|3[01])\./ || ip ~ /^100\.(6[4-9]|[7-9][0-9]|1[01][0-9]|12[0-7])\./ ||
         ip ~ /^(::1|fe80:|ff0|fc|fd)/
}'

# IP -> container (the last name seen for the address wins; restarts reuse addresses rarely within a run).
cp "$dir/containers.tsv" "$tmp/containers.tsv" 2>/dev/null || : >"$tmp/containers.tsv"

# IP -> names from every DNS answer in both captures (A, AAAA, and the CNAME chain's first name).
for pcap in "$dir/bridge.pcap" "$dir/uplink.pcap"; do
  fields "$pcap" 'dns.flags.response == 1' dns.qry.name dns.a dns.aaaa
done | awk -F'\t' '{
  n = split($2 "," $3, ips, ",")
  for (i = 1; i <= n; i++) if (ips[i] != "") print ips[i] "\t" $1
}' | sort -u >"$tmp/ipnames.tsv"

# Connection names seen in the first packets: TLS SNI (TCP and QUIC) and HTTP Host, per (source, destination, port).
for pcap in "$dir/bridge.pcap" "$dir/uplink.pcap"; do
  fields "$pcap" 'tls.handshake.extensions_server_name' ip.src ipv6.src ip.dst ipv6.dst tcp.dstport udp.dstport tls.handshake.extensions_server_name
  fields "$pcap" 'http.request' ip.src ipv6.src ip.dst ipv6.dst tcp.dstport udp.dstport http.host
done | awk -F'\t' '{ print $1 $2 "\t" $3 $4 "\t" $5 $6 "\t" $7 }' | sort -u >"$tmp/connnames.tsv"

# DNS queries per container.
fields "$dir/bridge.pcap" 'dns.flags.response == 0' ip.src dns.qry.name dns.qry.type |
  awk -F'\t' -v OFS='\t' '
    FILENAME == ARGV[1] { name[$1] = $2; next }
    $2 !~ /\./ || $2 ~ /\.dns\.podman$/ { next }
    { c = ($1 in name) ? name[$1] : $1; count[c OFS $2 OFS $3]++ }
    END { for (k in count) print k, count[k] }' "$tmp/containers.tsv" - |
  sort >"$out/dns-containers.tsv"

# DNS queries that reached the host's resolver.
fields "$dir/uplink.pcap" 'dns.flags.response == 0' dns.qry.name dns.qry.type |
  awk -F'\t' -v OFS='\t' '
    FILENAME == ARGV[1] { asked[$2] = 1; next }
    { count[$1 OFS $2]++; ctr[$1 OFS $2] = ($1 in asked) ? "yes" : "no" }
    END { for (k in count) print k, count[k], ctr[k] }' "$out/dns-containers.tsv" - |
  sort >"$out/dns-host.tsv"

# Connection starts (TCP SYN, and the first UDP datagram per flow) that leave the subnet.
starts() { # starts PCAP: src, dst, port, proto
  fields "$1" '(tcp.flags.syn == 1 && tcp.flags.ack == 0) || (udp && !dns && !mdns)' \
    ip.src ipv6.src ip.dst ipv6.dst tcp.dstport udp.dstport |
    awk -F'\t' -v OFS='\t' '{ print $1 $2, $3 $4, $5 $6, ($5 != "" ? "tcp" : "udp") }'
}

summarize_starts() { # summarize_starts PCAP WITH_SOURCE
  starts "$1" | awk -F'\t' -v OFS='\t' -v src="$2" -v keep="$keep_private" "$private_awk"'
    FILENAME == ARGV[1] { ctr[$1] = $2; next }
    FILENAME == ARGV[2] { names[$1] = (names[$1] == "" ? $2 : names[$1] "," $2); next }
    FILENAME == ARGV[3] { k = $1 OFS $2 OFS $3; cn[k] = (cn[k] == "" ? $4 : cn[k] "," $4); dn[$2 OFS $3] = (dn[$2 OFS $3] == "" ? $4 : dn[$2 OFS $3] "," $4); next }
    {
      if (!keep && private($2)) next
      who = (src == "yes") ? (($1 in ctr) ? ctr[$1] : $1) OFS : ""
      k = $1 OFS $2 OFS $3
      n = (k in cn) ? cn[k] : (($2 OFS $3) in dn) ? dn[$2 OFS $3] : ($2 in names) ? "dns:" names[$2] : "-"
      count[who $2 OFS $3 "/" $4 OFS n]++
    }
    END { for (k in count) print k, count[k] }' "$tmp/containers.tsv" "$tmp/ipnames.tsv" "$tmp/connnames.tsv" - |
    sort
}

summarize_starts "$dir/bridge.pcap" yes >"$out/out-containers.tsv"
summarize_starts "$dir/uplink.pcap" no >"$out/out-host.tsv"
awk -F'\t' -v OFS='\t' '$3 == "-" { print $1, $2, $4 }' "$out/out-host.tsv" >"$out/unnamed.tsv"

# Host processes from the ss poll: peer is ADDRESS:PORT (or [V6]:PORT).
if [[ -s $dir/ss.tsv ]]; then
  awk -F'\t' -v OFS='\t' -v keep="$keep_private" "$private_awk"'
    FILENAME == ARGV[1] { names[$1] = (names[$1] == "" ? $2 : names[$1] "," $2); next }
    FNR == 1 { next }
    {
      ip = $4; sub(/:[0-9]+$/, "", ip); gsub(/[][]/, "", ip); sub(/^::ffff:/, "", ip)
      if (!keep && private(ip)) next
      proc = $5; sub(/\/[0-9]+$/, "", proc)
      k = proc OFS $3 "/" $4 OFS ((ip in names) ? names[ip] : "-")
      if (!(k in first) || $1 < first[k]) first[k] = $1
      if ($2 > last[k]) last[k] = $2
    }
    END { for (k in first) printf "%s\t%s\t%s\n", k, strftime("%H:%M:%S", first[k]), strftime("%H:%M:%S", last[k]) }' \
    "$tmp/ipnames.tsv" "$dir/ss.tsv" | sort >"$out/processes.tsv"
fi

{
  echo "# egress summary of $dir"
  grep -E '^(started|stopped|bridge|subnet|uplink|resolvers)' "$dir/capture.info" || true
  echo "subnet of the stack: ${subnet:-unknown}"
  for f in dns-containers dns-host out-containers out-host processes unnamed; do
    [[ -f $out/$f.tsv ]] && printf '%-16s %s rows\n' "$f" "$(wc -l <"$out/$f.tsv")"
  done
} >"$out/README.txt"
cat "$out/README.txt"
