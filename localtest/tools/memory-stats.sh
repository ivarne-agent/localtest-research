#!/usr/bin/env bash
# One memory snapshot of the machine and the localtest stack, written to stdout.
#
#   sudo localtest/tools/memory-stats.sh LABEL > snapshot.txt
#
# Three sections:
#   1. Host totals: `free -m`, the /proc/meminfo fields that explain it, swap, and the memory.stat of the Podman cgroup
#      root (libpod_parent with the cgroupfs manager, machine.slice with systemd).
#   2. Per container (every running container of $CTR, default podman): `podman stats`, the cgroup's memory.current and
#      memory.stat anon/file/shmem/kernel, and the main process's VmRSS, RssAnon, RssFile and RssShmem.
#   3. Per host process (every process outside the container cgroups): RSS and PSS from /proc/<pid>/smaps_rollup for
#      the processes of interest (apps, studioctl, the Aspire AppHost, DCP, the dashboard, Chromium, Podman) and every
#      other process with PSS of 20 MiB or more, then the PSS sums.
# Root is needed to read smaps_rollup of other users' processes; without it those processes count as 0 and a note says
# so. All sizes are MiB (1024 * 1024 bytes) unless a column says otherwise.
set -euo pipefail

LABEL=${1:?usage: memory-stats.sh LABEL}
CTR=${CTR:-podman}
CG=/sys/fs/cgroup

mib() { awk -v b="$1" 'BEGIN { printf "%.0f", b / 1048576 }'; }
kb_mib() { awk -v k="$1" 'BEGIN { printf "%.0f", k / 1024 }'; }
# stat_field FILE FIELD: a memory.stat field in bytes, 0 when absent.
stat_field() { awk -v f="$2" '$1 == f { print $2; found = 1 } END { if (!found) print 0 }' "$1" 2>/dev/null || echo 0; }
# proc_kb FILE FIELD: a "Field: N kB" line in kB, 0 when absent or unreadable.
proc_kb() { awk -v f="$2:" '$1 == f { print $2; found = 1 } END { if (!found) print 0 }' "$1" 2>/dev/null || echo 0; }

echo "# memory-stats: $LABEL"
echo "time: $(date '+%Y-%m-%dT%H:%M:%S%z')  kernel: $(uname -r)  user: $(id -un)"
[[ $EUID -eq 0 ]] || echo "note: not root; other users' smaps_rollup are unreadable and count as 0"
echo

echo "## Host totals"
echo
echo '$ free -m'
free -m
echo
echo "/proc/meminfo (MiB):"
for f in MemTotal MemFree MemAvailable Buffers Cached Shmem Active\(file\) Inactive\(file\) Active\(anon\) Inactive\(anon\) \
  AnonPages Mapped SReclaimable SUnreclaim KernelStack PageTables Percpu VmallocUsed SwapTotal SwapFree; do
  printf '  %-15s %6s\n' "$f" "$(kb_mib "$(proc_kb /proc/meminfo "$f")")"
done
TOTAL_KB=$(proc_kb /proc/meminfo MemTotal)
FREE_KB=$(proc_kb /proc/meminfo MemFree)
BUFCACHE_KB=$(($(proc_kb /proc/meminfo Buffers) + $(proc_kb /proc/meminfo Cached) + $(proc_kb /proc/meminfo SReclaimable)))
USED_KB=$((TOTAL_KB - FREE_KB - BUFCACHE_KB))
KERNEL_KB=$(($(proc_kb /proc/meminfo SUnreclaim) + $(proc_kb /proc/meminfo KernelStack) + $(proc_kb /proc/meminfo PageTables) + $(proc_kb /proc/meminfo Percpu)))
echo "derived (MiB): MemTotal - MemAvailable = $(kb_mib $((TOTAL_KB - $(proc_kb /proc/meminfo MemAvailable))))" \
  "(free's \"used\" since procps-ng 4.0.1); MemTotal - MemFree - Buffers - Cached - SReclaimable = $(kb_mib "$USED_KB")" \
  "(the older \"used\", Shmem excluded); kernel non-reclaimable = SUnreclaim + KernelStack + PageTables + Percpu =" \
  "$(kb_mib "$KERNEL_KB")"
echo
PODROOT=
for d in "$CG/libpod_parent" "$CG/machine.slice"; do [[ -r $d/memory.stat ]] && PODROOT=$d && break; done
if [[ -n $PODROOT ]]; then
  echo "Podman cgroup root $PODROOT (MiB): memory.current $(mib "$(cat "$PODROOT/memory.current")")" \
    "anon $(mib "$(stat_field "$PODROOT/memory.stat" anon)")" \
    "file $(mib "$(stat_field "$PODROOT/memory.stat" file)")" \
    "shmem $(mib "$(stat_field "$PODROOT/memory.stat" shmem)")" \
    "kernel $(mib "$(stat_field "$PODROOT/memory.stat" kernel)")" \
    "file_mapped $(mib "$(stat_field "$PODROOT/memory.stat" file_mapped)")" \
    "inactive_file $(mib "$(stat_field "$PODROOT/memory.stat" inactive_file)")" \
    "active_file $(mib "$(stat_field "$PODROOT/memory.stat" active_file)")"
else
  echo "Podman cgroup root: not found or unreadable"
fi
echo

echo "## Per container"
echo
echo '$ podman stats --no-stream'
"$CTR" stats --no-stream --format '{{.Name}} {{.MemUsage}} {{.MemPerc}}' 2>/dev/null | sort || true
echo
printf '%-34s %8s %6s %6s %6s %6s %8s %6s %8s %8s %8s %8s\n' container current anon file shmem kernel pid restart \
  VmRSS RssAnon RssFile RssShmem
SUM_CUR=0 SUM_ANON=0 SUM_FILE=0 SUM_SHMEM=0 SUM_KERN=0 SUM_RSS=0 N=0
while read -r name pid restarts; do
  [[ -n $name ]] || continue
  cgpath=$(awk -F: '$1 == "0" { print $3 }' "/proc/$pid/cgroup" 2>/dev/null || true)
  dir=$CG$cgpath
  # A container with its own init sub-cgroup (systemd in the container, or Podman's "container" leaf): use the parent.
  [[ $(basename "$dir") == container ]] && dir=$(dirname "$dir")
  cur=$(cat "$dir/memory.current" 2>/dev/null || echo 0)
  anon=$(stat_field "$dir/memory.stat" anon)
  file=$(stat_field "$dir/memory.stat" file)
  shmem=$(stat_field "$dir/memory.stat" shmem)
  kern=$(stat_field "$dir/memory.stat" kernel)
  st=/proc/$pid/status
  rss=$(proc_kb "$st" VmRSS)
  printf '%-34s %8s %6s %6s %6s %6s %8s %6s %8s %8s %8s %8s\n' "$name" "$(mib "$cur")" "$(mib "$anon")" "$(mib "$file")" \
    "$(mib "$shmem")" "$(mib "$kern")" "$pid" "$restarts" "$(kb_mib "$rss")" "$(kb_mib "$(proc_kb "$st" RssAnon)")" \
    "$(kb_mib "$(proc_kb "$st" RssFile)")" "$(kb_mib "$(proc_kb "$st" RssShmem)")"
  SUM_CUR=$((SUM_CUR + cur)) SUM_ANON=$((SUM_ANON + anon)) SUM_FILE=$((SUM_FILE + file))
  SUM_SHMEM=$((SUM_SHMEM + shmem)) SUM_KERN=$((SUM_KERN + kern)) SUM_RSS=$((SUM_RSS + rss)) N=$((N + 1))
done < <("$CTR" ps --format '{{.Names}}' 2>/dev/null | sort | while read -r c; do
  "$CTR" inspect --format '{{.Name}} {{.State.Pid}} {{.RestartCount}}' "$c" 2>/dev/null || true
done)
printf '%-34s %8s %6s %6s %6s %6s %8s %6s %8s\n' "SUM ($N containers)" "$(mib "$SUM_CUR")" "$(mib "$SUM_ANON")" \
  "$(mib "$SUM_FILE")" "$(mib "$SUM_SHMEM")" "$(mib "$SUM_KERN")" - - "$(kb_mib "$SUM_RSS")"
echo

echo "## Per host process"
echo
# Every process: its cgroup decides container or host; PSS from smaps_rollup.
HOST_PSS=0 CTR_PSS=0 STACK_PSS=0 HIDDEN=0 ROWS=
declare -A CAT_PSS=()
for p in /proc/[0-9]*; do
  pid=${p#/proc/}
  cg=$(awk -F: '$1 == "0" { print $3 }' "$p/cgroup" 2>/dev/null || true)
  [[ -n $cg ]] || continue
  [[ -r $p/smaps_rollup ]] || { HIDDEN=$((HIDDEN + 1)); continue; }
  pss=$(proc_kb "$p/smaps_rollup" Pss)
  case $cg in
    /libpod_parent/* | /machine.slice/*) CTR_PSS=$((CTR_PSS + pss)); continue ;;
  esac
  HOST_PSS=$((HOST_PSS + pss))
  cmd=$(tr '\0\n' '  ' < "$p/cmdline" 2>/dev/null | cut -c1-110 || true)
  [[ -n $cmd ]] || continue
  # Classify by the program and its first argument only, so a shell whose script mentions a name does not match.
  head2=$(tr '\0\n' ' \n' < "$p/cmdline" 2>/dev/null | awk '{ print $1, $2, $3; exit }' || true)
  key=
  case "$head2" in
    *studioctl-server*) key=studioctl-server ;;
    */studioctl\ * | studioctl\ *) key=studioctl ;;
    *Aspire.Dashboard*) key=aspire-dashboard ;;
    */dcp\ * | *dcpctrl* | *dcpproc*) key=dcp ;;
    *apphost*.dll* | *apphost* | *AppHost*) key=aspire-apphost ;;
    *App2/bin/* | *App2.dll*) key=app2 ;;
    *App/bin/* | *App.dll* | *Altinn.Application*) key=app ;;
    *VBCSCompiler* | *MSBuild.dll*) key=build-server ;;
    "dotnet run --project") key=app-launcher ;;
    *chrom*) key=chromium ;;
    *dotnet*) key=dotnet-other ;;
    *podman* | *conmon*) key=podman ;;
  esac
  if [[ -n $key ]]; then
    CAT_PSS[$key]=$((${CAT_PSS[$key]:-0} + pss))
    [[ $key == podman || $key == build-server ]] || STACK_PSS=$((STACK_PSS + pss))
    ((pss >= 1024)) || continue
  elif ((pss < 20480)); then
    continue
  else
    key=other:$(cut -c1-16 "$p/comm")
  fi
  ROWS+=$(printf '%-8s %-22s %7s %7s %8s %8s %9s  %s' "$pid" "$key" "$(kb_mib "$(proc_kb "$p/status" VmRSS)")" \
    "$(kb_mib "$pss")" "$(kb_mib "$(proc_kb "$p/smaps_rollup" Pss_Anon)")" \
    "$(kb_mib "$(proc_kb "$p/smaps_rollup" Pss_File)")" "$(kb_mib "$(proc_kb "$p/smaps_rollup" Pss_Shmem)")" "$cmd")$'\n'
done
printf '%-8s %-22s %7s %7s %8s %8s %9s  %s\n' pid name RSS PSS Pss_Anon Pss_File Pss_Shmem command
printf '%s' "$ROWS" | sort -k4,4nr
echo
echo "PSS by category (MiB):"
for k in "${!CAT_PSS[@]}"; do printf '  %-18s %6s\n' "$k" "$(kb_mib "${CAT_PSS[$k]}")"; done | sort
echo "PSS sums (MiB): stack host processes (the categories above except podman and build-server) $(kb_mib "$STACK_PSS");" \
  "all host processes $(kb_mib "$HOST_PSS"); all container processes (incl. conmon) $(kb_mib "$CTR_PSS");" \
  "unreadable processes $HIDDEN"
