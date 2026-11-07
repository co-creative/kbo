#!/usr/bin/env python3
"""Owned by the co-creative platform (template dotnet-aca-v1); updates arrive as pull requests.

Delivery helper for .github/workflows/platform-deploy.yml. The app owns, and this script applies:
  - infra/app.bicep: the app's own resources in rg-<slug> (registry module bicep/app-resources), whose output
    'env' holds the capability settings (STORAGE__*, SQL__*, SERVICEBUS__*, SIGNALR__*);
  - platform.app.json: per component env, CPU and memory, replicas (web) or schedule and timeout (jobs);
  - the images built from platform.images.json.
Each component gets one update (one new revision): image + resources + scale/schedule + env.
Without platform.app.json only images are updated (the platform definition still holds the configuration).

  resources              deploy infra/app.bicep (if present); write its env to .platform/capability-env.json
  validate               check platform.app.json: components exist, no reserved names, within the ceilings
  apply                  update each component (images from platform.images.json for this commit)
Environment: SLUG, RG, ACR, SHA (set by the workflow); az logged in as the app's CI identity.
DRY_RUN=1 prints the updates instead of applying them.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys

SLUG, RG = os.environ['SLUG'], os.environ['RG']
CONFIG = 'platform.app.json'
AZ = shutil.which('az') or 'az'  # az.cmd on Windows (local dry runs)
CAP_ENV = '.platform/capability-env.json'

# Environment variables the platform sets; the app can't set or remove them.
RESERVED = {'AZURE_CLIENT_ID', 'APP_SLUG', 'PLATFORM_COMPONENT', 'APPLICATIONINSIGHTS_CONNECTION_STRING',
            'ASPNETCORE_HTTP_PORTS', 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', 'AI__ENDPOINT', 'AI__DEPLOYMENT'}


def reserved(name: str) -> bool:
    return name in RESERVED or name.startswith('PLATFORM_')


def fail(msg: str) -> None:
    print(f'::error::{msg}')
    sys.exit(1)


def az(*args: str, check: bool = True):
    p = subprocess.run([AZ, *args, '-o', 'json'], capture_output=True, text=True)
    if p.returncode != 0:
        if check:
            fail(f'az {" ".join(args[:4])} failed: {p.stderr.strip()[:800]}')
        return None
    return json.loads(p.stdout) if p.stdout.strip() else None


def summary(line: str) -> None:
    with open(os.environ.get('GITHUB_STEP_SUMMARY', os.devnull), 'a', encoding='utf-8') as f:
        f.write(line + '\n')


def load_config() -> dict | None:
    if not os.path.isfile(CONFIG):
        return None
    with open(CONFIG, encoding='utf-8') as f:
        return json.load(f)


def capability_env() -> dict:
    if not os.path.isfile(CAP_ENV):
        return {}
    with open(CAP_ENV, encoding='utf-8') as f:
        return json.load(f)


def components(config: dict) -> list[tuple[str, str, dict]]:
    """[(kind, azure name, settings)] for the components declared in platform.app.json."""
    out = []
    if config.get('web') is not None:
        out.append(('web', f'{SLUG}-web', config['web'] or {}))
    for name, settings in (config.get('jobs') or {}).items():
        out.append(('job', f'{SLUG}-{name}', settings or {}))
    return out


def previous_env(kind: str, job: str) -> set:
    """Names the app declared in the previously delivered platform.app.json (git, at tag lastDeliverySha).
    Only those can be removed: anything else on the component (platform settings, current or future) is left
    alone. Empty when there's no previous delivery with a platform.app.json."""
    sha = ((az('group', 'show', '-n', RG, '--query', 'tags', check=False) or {}).get('lastDeliverySha') or '')
    if not sha:
        return set()
    p = subprocess.run(['git', 'show', f'{sha}:{CONFIG}'], capture_output=True, text=True)
    if p.returncode != 0:
        return set()
    prev = json.loads(p.stdout)
    settings = prev.get('web') if kind == 'web' else (prev.get('jobs') or {}).get(job)
    return set(((settings or {}).get('env') or {}).keys())


def show(kind: str, name: str):
    return az('containerapp', *(['job'] if kind == 'job' else []), 'show', '-g', RG, '-n', name, check=False)


# ---- resources --------------------------------------------------------------------------------------------
def cmd_resources() -> None:
    os.makedirs('.platform', exist_ok=True)
    if not os.path.isfile('infra/app.bicep'):
        json.dump({}, open(CAP_ENV, 'w'))
        print('no infra/app.bicep: no app-owned resources')
        return
    out = az('deployment', 'group', 'create', '-g', RG, '-n', 'app-resources', '--template-file', 'infra/app.bicep',
             '--query', 'properties.outputs')
    env = ((out or {}).get('env') or {}).get('value') or {}
    bad = [k for k in env if reserved(k)]
    if bad:
        fail(f"infra/app.bicep output 'env' sets reserved names: {', '.join(bad)}")
    json.dump(env, open(CAP_ENV, 'w'))
    summary(f'- app resources deployed (`infra/app.bicep`): {", ".join(sorted(env)) or "no settings"}')


# ---- validate ---------------------------------------------------------------------------------------------
def to_gib(value: str) -> float:
    return float(str(value).lower().removesuffix('gi'))


def cmd_validate() -> None:
    config = load_config()
    if config is None:
        print(f'no {CONFIG}: configuration stays in the platform definition (images only)')
        return
    tags = (az('group', 'show', '-n', RG, '--query', 'tags') or {})
    ceiling_cpu = float(tags['ceiling.cpu']) if 'ceiling.cpu' in tags else None
    ceiling_mem = to_gib(tags['ceiling.memory']) if 'ceiling.memory' in tags else None
    ceiling_rep = int(tags['ceiling.maxReplicas']) if 'ceiling.maxReplicas' in tags else None
    errors = []
    for kind, name, s in components(config):
        if show(kind, name) is None:
            errors.append(f"{name}: no such {'web app' if kind == 'web' else 'job'}; declare the component in "
                          f"apps/{SLUG}/main.bicepparam (platform repository) first")
            continue
        for key in (s.get('env') or {}):
            if reserved(key):
                errors.append(f'{name}: env {key} is set by the platform')
        if 'cpu' in s and ceiling_cpu is not None and float(s['cpu']) > ceiling_cpu:
            errors.append(f"{name}: cpu {s['cpu']} is above the ceiling {ceiling_cpu}")
        if 'memory' in s and ceiling_mem is not None and to_gib(s['memory']) > ceiling_mem:
            errors.append(f"{name}: memory {s['memory']} is above the ceiling {tags['ceiling.memory']}")
        if kind == 'web' and 'maxReplicas' in s and ceiling_rep is not None and int(s['maxReplicas']) > ceiling_rep:
            errors.append(f"{name}: maxReplicas {s['maxReplicas']} is above the ceiling {ceiling_rep}")
        if 'cpu' in s and 'memory' in s and abs(to_gib(s['memory']) - 2 * float(s['cpu'])) > 1e-9:
            errors.append(f"{name}: memory must be 2 GiB per vCPU (cpu {s['cpu']} -> {2 * float(s['cpu']):g}Gi)")
    if errors:
        fail('platform.app.json: ' + '; '.join(errors) +
             ' (ceilings are set in the platform definition; ask the platform to raise them)')
    print(f'{CONFIG}: valid')


# ---- apply ------------------------------------------------------------------------------------------------
def images() -> dict:
    acr, sha = os.environ['ACR'], os.environ['SHA']
    out = {}
    for img in json.load(open('platform.images.json', encoding='utf-8')):
        target = img.get('deploy', '')
        tag = f"{acr}/{SLUG}/{img['name']}:{sha}"
        if target == 'web':
            out[('web', f'{SLUG}-web')] = tag
        elif target.startswith('job:'):
            out[('job', f'{SLUG}-{target[4:]}')] = tag
    return out


def cmd_apply() -> None:
    config = load_config()
    imgs = images()
    cap = capability_env()
    targets = {}
    if config is not None:
        for kind, name, s in components(config):
            targets[(kind, name)] = s
    for key in imgs:
        targets.setdefault(key, None)  # image-only update
    for (kind, name), s in targets.items():
        live = show(kind, name)
        if live is None:
            print(f'::warning::{name} is not provisioned (declare it in apps/{SLUG}/main.bicepparam); skipped')
            continue
        args = ['containerapp', *(['job'] if kind == 'job' else []), 'update', '-g', RG, '-n', name]
        base = len(args)
        if (kind, name) in imgs:
            args += ['--image', imgs[(kind, name)]]
        if s is not None:
            if 'cpu' in s:
                args += ['--cpu', str(s['cpu'])]
            if 'memory' in s:
                args += ['--memory', str(s['memory'])]
            if kind == 'web':
                if 'minReplicas' in s:
                    args += ['--min-replicas', str(s['minReplicas'])]
                if 'maxReplicas' in s:
                    args += ['--max-replicas', str(s['maxReplicas'])]
            else:
                if 'schedule' in s:
                    args += ['--cron-expression', s['schedule']]
                if 'replicaTimeout' in s:
                    args += ['--replica-timeout', str(s['replicaTimeout'])]
            desired = {**cap, **{k: str(v) for k, v in (s.get('env') or {}).items()}}
            current = {e['name'] for e in live['properties']['template']['containers'][0].get('env') or []}
            # Remove only settings this repository declared before and dropped since (never platform settings).
            dropped = previous_env(kind, name[len(SLUG) + 1:]) - set(desired)
            remove = sorted(n for n in current if n in dropped and not reserved(n))
            if desired:
                args += ['--set-env-vars', *[f'{k}={v}' for k, v in desired.items()]]
            if remove:
                args += ['--remove-env-vars', *remove]
        if len(args) == base:
            continue
        if os.environ.get('DRY_RUN'):
            print('DRY RUN: az ' + ' '.join(args))
            continue
        az(*args, '--query', 'name')
        summary(f"- {name}: {'`' + imgs[(kind, name)] + '`' if (kind, name) in imgs else 'configuration'}"
                f"{' + configuration' if s is not None and (kind, name) in imgs else ''}")
        print(f'{name}: updated')


if __name__ == '__main__':
    {'resources': cmd_resources, 'validate': cmd_validate, 'apply': cmd_apply}[sys.argv[1]]()
