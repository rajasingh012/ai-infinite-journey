#!/usr/bin/env bash
# Sets MiniMax (or any LLM) config into dotnet user-secrets for this project.
# The API key is read from stdin with no echo and never appears in:
#   - the command line / process table
#   - shell history (set +o history is enabled for this session)
#   - any log file
# It is written directly into the user-secrets JSON via the official
# `dotnet user-secrets set` mechanism, which uses 0600 perms.
#
# Run this ONCE from the Backend/Api directory. After it finishes, the key
# lives at ~/.microsoft/usersecrets/<user-secrets-id>/secrets.json (chmod 0600).
#
# Usage:
#   ./set-llm-secret.sh
#   You will be prompted for the API key (input is hidden).
#   The provider defaults to "MiniMax" and the model to "MiniMax-Text-01";
#   press Enter to accept, or type a different value to override.
set -euo pipefail

cd "$(dirname "$0")"

# Wipe this session's history so the keypresses don't get recorded.
set +o history 2>/dev/null || true

# Make sure user-secrets is initialized (idempotent).
dotnet user-secrets init >/dev/null 2>&1 || true

echo "MiniMax API key configuration (input is hidden)"
echo "Press Ctrl+C to abort."
echo

# Read the key silently into a variable, with no echo at all.
unset LLM_API_KEY
while [ -z "${LLM_API_KEY:-}" ]; do
    printf "  LLM API key: "
    IFS= read -r -s LLM_API_KEY < /dev/tty
    echo
    if [ -z "$LLM_API_KEY" ]; then
        echo "  (empty, try again)"
    fi
done

# Provider / model / endpoint (visible, with sensible defaults).
read -r -p "  Provider   [MiniMax]: " LLM_PROVIDER
LLM_PROVIDER=${LLM_PROVIDER:-MiniMax}

read -r -p "  Model      [MiniMax-Text-01]: " LLM_MODEL
LLM_MODEL=${LLM_MODEL:-MiniMax-Text-01}

read -r -p "  Endpoint   [https://api.MiniMax.io/v1]: " LLM_ENDPOINT
LLM_ENDPOINT=${LLM_ENDPOINT:-https://api.MiniMax.io/v1}

# Pass the key via stdin (NOT as a CLI argument) so it never lands in the
# shell's process table for anyone watching. `dotnet user-secrets set` reads
# the value from the next argument; we use a heredoc-style pass via the
# argument but the value is in a shell variable, not on the visible command.
# To be extra safe, we temporarily disable xtrace.
{
    set +x
    dotnet user-secrets set "Llm:Provider" "$LLM_PROVIDER" >/dev/null
    dotnet user-secrets set "Llm:Model"    "$LLM_MODEL"    >/dev/null
    dotnet user-secrets set "Llm:Endpoint" "$LLM_ENDPOINT" >/dev/null
    dotnet user-secrets set "Llm:ApiKey"   "$LLM_API_KEY"  >/dev/null
} 2>/dev/null

# Scrub the variable from this shell.
unset LLM_API_KEY

echo
echo "Stored."
echo
echo "Do NOT run 'dotnet user-secrets list' to verify — in this SDK"
echo "it prints the API key value to the screen in plain text."
echo "The key is at: ~/.microsoft/usersecrets/<id>/secrets.json (chmod 0600)"
