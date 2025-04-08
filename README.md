# YetAnotherRandomPaintingSwap V1.0.0 (R.E.P.O.)

YetAnotherRandomPaintingSwap randomly replaces all paintings in the game with images of your choice.

It is a fork of the RandomPaintingSwap, FittingPaintings and AnotherRandomPaintingSwap mods, including all of the functionality:

- Randomizes paintings across the game using images in three separate aspect ratios.

- Applies a customizable grunge filter to the paintings.

- Synchronizes the seed between host and clients so that all players see consistent replacements.

> ## Installation
### Manual Installation
1. Go to the root folder of your game.
2. Open the `BepInEx\plugins` folder.
3. Place the `RandomPaintingSwap.dll` file of the plugin inside this folder.

### Adding Custom Images
1. In the `plugins` folder where you placed the `RandomPaintingSwap.dll` file, create a folder named `RandomLandscapePaintingSwap_Images`, `RandomSquarePaintingSwap_Images`, and `RandomPortraitPaintingSwap_Images`.
2. Place your images inside this folder. These images will be used to randomly replace the paintings in the game.

* **Landscape** paintings go in any folder named `RandomLandscapePaintingSwap_Images` (2:1)
* **Square** paintings go in any folder named `RandomSquarePaintingSwap_Images` (1:1)
* **Portrait** paintings go in any folder named `RandomPortraitPaintingSwap_Images` (It varies but between 9:16 and 3:4 will look fine)



#### Images Format
- `.png` and `.jpg/.jpeg` images are supported.

## Other
Github: https://github.com/snowtyler/YetAnotherRandomPaintingSwap<br>
Author: **snowtyler**

Thunderstore: https://thunderstore.io/c/repo/p/ZeroTails/FittingPaintings/<br>
Author: **ZeroTails**

Github: https://github.com/Phnod/AnotherRandomPaintingSwap<br>
Author: **Phnod**

Forked from (Who did most of the work)<br>
Github: https://github.com/GabziDev/RandomPaintingSwap<br>
Author: **Gabzdev**
