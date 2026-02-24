# FBX-ASCII-Unity-Rotation-Tool

A simple console tool to rebase ASCII FBX (and Unity prefab) file local rotations and scales via packing ``"Lcl Rotation"`` and ``Lcl Scaling`` into ``"Geometric"`` properties and reseting the original locals.

It also updates Unity prefab files to reset the ``Transform:`` property ``m_LocalRotation`` value to `0,0,0` and ``m_LocalScale`` value to `0,0,0,1`.

Especially useful for 'fixing' submesh rotations for merged FBX files in situations where auto-parsing AI junk tools *cough* _**Unity Asset Store**_ *cough* aren't able to discern that submeshes might want to be rotated to their real values, not a rebased `0,0,0`/`0,0,0,1`.

It will check all .fbx *(and check if they are ASCII format)* and .prefab files in the same directory as the tool, and proceed to check the gemoetry/transform nodes within and update them per above.

It will then reset the Local Rotation to ``0,0,0``/``0,0,0,1`` and Local Scale to ``1,1,1`` and save a copy of the files with `_fixed` appended to the filenames.

<br>

Licensed under [MIT License](https://github.com/vectorcmdr/FBX-ASCII-Rebase-Rotation-Tool/blob/b1057e73d1db15f4f8738a6ecbb86a7a28b767d6/LICENSE).

Feel free to extend it via a PR, build upon it for yourself, or integrate it into your workflow as-is.
