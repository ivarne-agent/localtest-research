# shellcheck shell=bash
# Container names for the smoke tests, under either runtime of the stack. Source it; it defines two functions and sets
# nothing else:
#
#   localtest_runtime          prints the container CLI: $CTR when set, otherwise podman, otherwise docker; status 1
#                              (and a reason on stderr) when there is none.
#   localtest_container NAME   prints the running container of the manifest component NAME (storage, authorization,
#                              postgres, localtest, ...); status 1 and a reason on stderr when there is no such
#                              container, or more than one, so a caller can skip its check:
#     1. LOCALTEST_CONTAINER_<NAME> (NAME upper-cased, `-` as `_`) when set, and for storage and receipt also the older
#        STORAGE_CONTAINER and RECEIPT_CONTAINER; it must name an existing container.
#     2. studioctl's `localtest-v2-<name>` (the localtest-v2 loader's container prefix plus the name) when it is running.
#     3. The Aspire package's `<resource>-<suffix>` (the suffix a hash of the AppHost's path, `storage-0de70ea0`): the
#        resource is the manifest name, except the localtest app, which is `localtest-app` beside the parent resource
#        `localtest`. Exactly one running container may match; two AppHosts' stacks at once are an error.
#
#   . "$HERE/../../smoke/lib/containers.sh"
#   if RT=$(localtest_runtime) && C=$(localtest_container storage); then "$RT" logs "$C"; else echo "skip ..."; fi

localtest_runtime() {
  if [[ -n "${CTR:-}" ]]; then
    printf '%s\n' "$CTR"
  elif command -v podman >/dev/null 2>&1; then
    echo podman
  elif command -v docker >/dev/null 2>&1; then
    echo docker
  else
    echo "no container runtime (podman or docker; or set CTR)" >&2
    return 1
  fi
}

localtest_container() {
  local name=${1:?localtest_container: a component name}
  local rt var override resource matches count
  rt=$(localtest_runtime) || return 1

  var=LOCALTEST_CONTAINER_$(printf '%s' "$name" | tr '[:lower:]-' '[:upper:]_')
  override=${!var:-}
  case $name in
    storage) override=${override:-${STORAGE_CONTAINER:-}} ;;
    receipt) override=${override:-${RECEIPT_CONTAINER:-}} ;;
  esac
  if [[ -n "$override" ]]; then
    if "$rt" container inspect "$override" >/dev/null 2>&1; then
      printf '%s\n' "$override"
      return 0
    fi
    echo "no container $override ($var)" >&2
    return 1
  fi

  # studioctl: a fixed name. The name filter narrows (Docker's sees a leading slash, so it is not anchored); the exact
  # match decides.
  if "$rt" ps --filter "name=localtest-v2-$name" --format '{{.Names}}' 2>/dev/null | grep -qx "localtest-v2-$name"; then
    printf '%s\n' "localtest-v2-$name"
    return 0
  fi

  # Aspire: <resource>-<suffix>. The exact match keeps arbeidsflate from matching arbeidsflate-bff-*.
  resource=$name
  [[ $name == localtest ]] && resource=localtest-app
  matches=$("$rt" ps --filter "name=$resource-" --format '{{.Names}}' 2>/dev/null | grep -E "^$resource-[[:alnum:]]+\$")
  count=$(printf '%s' "$matches" | grep -c .)
  if [[ $count == 1 ]]; then
    printf '%s\n' "$matches"
    return 0
  fi
  if [[ $count == 0 ]]; then
    echo "no running container for $name (localtest-v2-$name or $resource-<suffix>; set $var)" >&2
  else
    echo "$count running containers for $name (${matches//$'\n'/ }); set $var" >&2
  fi
  return 1
}
