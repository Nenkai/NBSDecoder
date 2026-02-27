# NBSDecoder

NewBasis (.nbs) decoder for files found in certain chinese games.

The dll included is their altered version of ffmpeg/vp9, which should be fine here considering it was minor changes. 

License to ffmpeg is also included in this repository.

## Research Notes

Refer to [NewBasis.cs](NBSDecoder/NewBasis.cs).

Also [010 Editor Template](https://github.com/Nenkai/010GameTemplates/blob/main/Netease/NBS_NewBasis.bt)

## Current State

Fully functional.

May error with videos with alpha channels (but don't actually use the alpha, can be ignored).

## Building

.NET SDK 10.0
