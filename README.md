# FBX-ASCII-Unity-Rotation-Tool

A simple(?) console tool to rebase nested and merged **ASCII** FBX (and Unity prefab) files.<br>
It's got a fairly specific use, but if you need it you need it.

It handles local rotations and scales via packing ``Lcl Rotation`` and ``Lcl Scaling`` into ``Geometric`` properties and reseting the original locals.<br>
It also checks for specific rotation clamping, floating point (near-zero) issues, scale inversions (mirroring before merging) and other fringe cases and deals with them by catching and convering, mirroring verts, reversing polygon windings, etc.

Any Unity prefab files in the directory also have their ``Transform:`` property ``m_LocalRotation`` value set to `0,0,0,1` and ``m_LocalScale`` value set to `1,1,1` to accomodate the mesh changes.

Especially useful for 'fixing' submesh rotations for merged FBX files in situations where auto-parsing AI junk tools *cough* _**Unity Asset Store**_ *cough* aren't able to discern that submeshes might want to be rotated to their real values, not a rebased `0,0,0`/`0,0,0,1`.

It will check all .fbx *(and check if they are ASCII format)* and .prefab files in the same directory as the tool, and proceed to check the gemoetry/transform nodes within and update them per above and produce a new file with "_fixed" appended to the end for each of them.

<br>

Licensed under [MIT License](https://github.com/vectorcmdr/FBX-ASCII-Rebase-Rotation-Tool/blob/b1057e73d1db15f4f8738a6ecbb86a7a28b767d6/LICENSE).

Feel free to extend it via a PR, build upon it for yourself, or integrate it into your workflow as-is.
