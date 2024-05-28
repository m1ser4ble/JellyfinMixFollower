# JellyfinMixFollower

a jellyfin plugin that constructs and updates playlists by fetching from mixs like billboard chart, melon chart

## Fetching message format

JellyfinMixFollower only accept json data like below

```
{
  "name" : "Billboard Hot 100™",
  "chart" : [
    {"title": "I Had Some Help", "artist": "Post Malone Featuring Morgan Wallen"},
    {"title", "", "artist": ""}
  ]
}
```
