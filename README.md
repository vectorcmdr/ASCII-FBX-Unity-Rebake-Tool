# ASCII FBX Unity Rebake Tool

A console tool to rebake nested and merged **ASCII** FBX (and rebase Unity prefab) files.<br>

### How It Works:
It essentially does what Maya/3DS/Blender do when you set xform before export, but without introducing program specific weirdness, changing mat orders, geometry ordering, changing FBX versions, hashes, IDs, or anything else like that.

It does this by processesing ASCII format FBX files in it's run directory and bakes original local rotation, scale etc. (``Properties70``) into geometry properties (``Geometry``) and resets the original properties.<br>
It also processes Unity prefab (YAML) files that correspond to the FBX files and resets ``m_LocalRotation`` to identity quaternion, ``m_LocalScale`` to ``(1,1,1)``, ``m_LocalEulerAnglesHint`` to ``(0,0,0)``.
The modified output files are created in a subdirectory named "baked".

_...really earning my pseudonym with this one..._

### Purpose:
It's was originally intended for use on merged meshes where child geometry/submeshes have their original rotations and scales (including engine managed inversion) within a reset parent.
It's got a fairly specific use, but if you need it you need it.

Particular use case where this is helpful: ``Build Unity Scene``->``Export Parent GameObject with meshes inside as FBX``->``Use Unity Asset Store Validator``.

Unity has a new junk AI validation process on their publishing end that will auto reject nested, non-reset transforms because they couldn't comprehend a reason why this might actually be desired by a level designer, environment artist, tech artist, etc. 🤷‍♂️ Just normal Unity L's.

### Usage:
Drop the executable and it's fellow files into a directory with your ASCII format FBX files and double click the executable.<br>
You can also call it from commandline (obviously) so that you can redirect the output window to a file.<br>
It will run giving feedback on it's actions taken (it's fairly verbose) and output the files to a new 'baked' subdirectory.

<br>

Licensed under [MIT License](https://github.com/vectorcmdr/FBX-ASCII-Rebase-Rotation-Tool/blob/b1057e73d1db15f4f8738a6ecbb86a7a28b767d6/LICENSE).

Feel free to extend it via a PR, build upon it for yourself, or integrate it into your workflow as-is.

It obviously comes with no warranty.
I've used it on many large and live projects, but it's always a good idea to backup your projects before you use it, just in case.
