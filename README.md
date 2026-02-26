# NBSDecoder

NewBasis (.nbs) decoder for files found in certain chinese games.

The dll included is their altered version of ffmpeg/vp9, which should be fine here considering it was minor changes. 

License to ffmpeg is also included in this repository.

## Research Notes

Refer to [NewBasis.cs](NBSDecoder/NewBasis.cs).

## Current State

Most frames decode fine, except ones with alpha (please advise if someone figures it out).

## Building

.NET SDK 10.0
