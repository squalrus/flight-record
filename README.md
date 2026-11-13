# Flight Record (working title)

A local-multiplayer (same machine, shared screen), vertically scrolling shoot 'em up whose levels are generated live from whatever is playing on a real turntable (or CD player, MP3 player, or any line-level source). One side of a vinyl record is one level.

## The idea in one paragraph

Audio from any source is split in hardware: one leg goes to your speakers with zero added latency, the other goes to a computer. The computer analyzes the audio in real time (beats, bass/mid/treble energy, loudness) and turns it into enemy waves, bullet patterns, and pacing. A fingerprinting service identifies the song, Discogs identifies the pressing and its tracklist, and the game uses that to know where in the side you are, so the side ends in a boss and a level-complete screen as the run-out groove arrives.

## Status

Pre-prototype. The plan is to prove the risky parts with small standalone experiments (Python), then merge into a fork of an existing Unity top-down space shooter that is being reworked into a vertical scroller with local co-op.

## Docs

| Doc | What it covers |
| --- | --- |
| [docs/concept-and-design.md](docs/concept-and-design.md) | Game concept, audio-to-gameplay mapping, level structure, local co-op design intent |
| [docs/flightrec/architecture.md](docs/flightrec/architecture.md) | Modules, message contract, latency model, local co-op notes |
| [docs/flightrec/hardware.md](docs/flightrec/hardware.md) | Signal chain, splitter options, interface requirements |
| [docs/flightrec/experiments.md](docs/flightrec/experiments.md) | **The proof-out plan**: numbered experiments with goals and success criteria |
| [docs/unity/unity-fork-plan.md](docs/unity/unity-fork-plan.md) | Converting the top-down shooter to vertical scrolling, local co-op, integration approach |
| [docs/risks-and-open-questions.md](docs/risks-and-open-questions.md) | Known risks, legal/ToS notes, decisions still to make |
| [docs/naming.md](docs/naming.md) | Name candidates and notes |
| [docs/decisions.md](docs/decisions.md) | Decisions made and still open |
| [BACKLOG.md](BACKLOG.md) | **Ordered task list** with dependencies and "done when" |
| [AGENTS.md](AGENTS.md) | Instructions for coding agents: constraints, conventions, what to ask a human |

## Getting started

Humans and coding agents: read [AGENTS.md](AGENTS.md), then start at the top of [BACKLOG.md](BACKLOG.md).

To try the explorer (setup in [experiments/README.md](experiments/README.md#setup); API keys in `.env.local`):

```sh
python run.py list       # songs in samples/_songs and their short names
python run.py kalax      # replay one: speakers, identification, Discogs release panel
python run.py mic        # listen on the mic / line in (FR_AUDIO_DEVICE)
```

`--plain` skips identification and Discogs, `--audd` uses AudD, `--quiet` replays without sound, and anything after `--` goes to `explore.py`.

## Repo layout

```
/docs                 these documents
/experiments
  /01-capture-visualizer
  /02-onset-features
  /03-fingerprint
  /04-discogs-lookup
  /05-side-tracking
  /06-message-bridge
                      (07 local co-op and 08 vertical slice live in /unity)
/tools                shared helpers (audio device listing, recording scripts)
/unity                the forked Unity project (or a submodule)
/samples              short recorded test clips (keep small; mind licensing)
```
