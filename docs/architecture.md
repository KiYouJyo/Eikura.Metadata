# Architecture

## Runtime layers

`Eizo.Metadata` intentionally separates deterministic path recognition from online
metadata lookup.

```text
Media path / filename
        |
        v
Eizo.Metadata.Recognition
        |
        | RecognitionResult
        v
Eizo.Metadata.Core
        |
        | MetadataSearchRequest
        | MetadataResolution
        v
Eizo.Metadata.Providers
        |
        +--> Bangumi public v0
        +--> TMDB v3
```

Dependencies only point downward in that diagram:

- `Core -> Recognition`
- `Providers -> Core`
- `Recognition -> nothing in this repository`

Recognition must never reference Core or Providers.

## Recognition boundary

Recognition converts noisy path text into structured candidates. It is deterministic,
offline, Unicode-safe and deliberately conservative when evidence conflicts.

It owns:

- title candidates;
- media-kind hints;
- season/cour/episode/special structure;
- year and release-noise extraction;
- confidence, ambiguity and explainable evidence.

It does not own canonical external IDs, artwork, summaries or network calls.

## Core metadata boundary

Core translates Recognition output into provider-neutral search intent and external
metadata decisions.

`MetadataSearchRequest.FromRecognition` carries up to four ranked title candidates plus
year, media kind, season and episode hints.

`MetadataResolver`:

1. queries providers independently;
2. converts provider failures into `MetadataProviderError` instead of failing the whole
   enrichment pass;
3. de-duplicates provider/item IDs;
4. scores title similarity, year, media-kind compatibility and provider result rank;
5. requires both an absolute confidence threshold and a winner margin before automatic
   resolution.

This keeps uncertain matches reviewable instead of inventing certainty.

## Provider boundary

Providers implement `IMetadataProvider` and map remote schemas into Core contracts.

### Bangumi

Uses the public `api.bgm.tv/v0` surface. The host supplies an application-specific
User-Agent and may optionally supply a bearer token. Basic search/details remain
separate from any future user-account OAuth functionality.

### TMDB

Uses the v3 API with an application read-access bearer token supplied by Eizo. Movie and
TV search paths remain distinct because TMDB models them as separate resources.

## Cache boundary

`CachedMetadataProvider` decorates any provider without changing its implementation.

- search results: short TTL;
- subject details: longer TTL;
- episode lists: medium TTL;
- memory cache: useful for one process/session;
- file cache: hashed keys, JSON envelopes, expiration and atomic replacement.

Cache corruption is treated as a miss. Cache contents must never become required for
Recognition or playback.

## Runtime packaging

All product assemblies under `src/Eizo.Metadata.*` share the existing external
Metadata update unit. The runtime manifest enumerates the modules included in a release.

For Eizo 0.3.6 compatibility, the on-disk technical component identity and anchor remain
`Eizo.Recognition` / `Eizo.Metadata.Recognition.dll`. Adding Core and Providers does
not make the package self-contained and does not bypass the pending -> restart -> active
component activation chain.

## Host integration rule

Eizo should use metadata enrichment asynchronously:

```text
scan / WebDAV listing
       |
       +--> Recognition -> catalog immediately usable
       |
       +--> Metadata resolver -> cached network enrichment
                                  |
                                  +--> canonical title
                                  +--> provider IDs
                                  +--> artwork
                                  +--> summary
                                  +--> episode metadata
```

A slow or unavailable provider must not delay opening the media library or playing a
known media item.


## Artwork enrichment layer

Artwork enrichment is intentionally separate from identity resolution. A resolved
subject can keep its existing provider metadata while missing visual assets are
filled by ordered `IMetadataArtworkProvider` implementations.

The initial provider order is:

1. AniList for animation `bannerImage` and cover artwork.
2. TMDB for backdrop/poster fallback when a TMDB read token is available.

`MetadataArtworkResolver` only fills missing artwork fields and isolates provider
failures. `CachedMetadataArtworkProvider` reuses the existing metadata cache so
episode-by-episode library scans do not repeat the same remote artwork lookup.
