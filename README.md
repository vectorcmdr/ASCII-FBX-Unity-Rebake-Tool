# FBX-ASCII-Rebase-Rotation-Tool

A simple console tool to rebase ASCII FBX rotations via packing ``"Lcl Rotation"`` into ``"GeometricRotation"`` and reseting ``"Lcl Rotation"``.

Especially useful for 'fixing' submesh rotations for merged FBX files in situations where auto-parsing AI junk tools *cough* _**Unity Asset Store**_ *cough* aren't able to discern that submeshes might want to be rotated to their real values, not a rebased `0,0,0`.

It will check all .fbx files in the same directory as the tool, discern if they are ASCII format and if so it will proceed to check the gemoetry nodes within for a Local Rotation value (``"Lcl Rotation"``) and pack that into it's Geometry Rotation ``"GeometricRotation"`` value instead (or create one if not present).

It will then reset the Local Rotation to ``0,0,0 ZXY`` and save a copy of the file with `_fixed` appended to the filename.

<br>

Licensed under [MIT License](https://github.com/vectorcmdr/FBX-ASCII-Rebase-Rotation-Tool/blob/b1057e73d1db15f4f8738a6ecbb86a7a28b767d6/LICENSE).

Feel free to extend it via a PR, build upon it for yourself, or integrate it into your workflow as-is.
