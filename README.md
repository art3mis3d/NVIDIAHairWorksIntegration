# NVIDIA Hair Works Integration
![grass](doc/grass.gif)
![mite](doc/mite.gif)

A Unity integration of NVIDIA HairWorks. You can display Hair authored in MAYA or 3ds Max within Unity.  

## How To use
**Requires Unity 5.5 or later, Windows DX11 only**  
Installation is somewhat complicated. 
As the Hairworks SDK is not allowed to be redistributed, you must obtain it from NVIDIA's developer website and import it into the Unity project. 
The 3-step procedure is explained below. The current integration is based on HairWorks SDK version 1.1.1.

1.  Get the HairWorks SDK 1.1.1
  * https://developer.nvidia.com/gameworksdownload NVIDIA HairWorks -> HairWorks 1.1.1 here
  * A NVIDIA developer account is required to download the SDK. Creating an account is free. The amount of time before you can access gameworks features may vary.
  * MAYA and 3ds Max authoring plugins can also be obtained from here
  
2. Import the following DLL's in the HairWorks SDK you downloaded into the Unity project.
  * Copy HairWorks-r1_1_1-62 / HairWorks / bin / win64 / GFSDK_HairWorks.win64.dll into Assets / UTJ / Plugins / x86_64 (Only required if you target 64 bit)
  * Copy HairWorks-r1_1_1-62 / HairWorks / bin / win32 / GFSDK_HairWorks.win32.dll into Assets / UTJ / Plugins / x86 (Only required if you target 32 bit)

### Build Instructions
The hair shader used by the integration is not a normal Unity shader, if you want to customize the shader then you need to write an HLSL shader yourself or modify the existing one in the Visual Studio project. 
If you want to build the shader and the plug-in, Copy the HairWorks-r1_1_1-62/HairWorks/include folder into the Plugin folder.
Open the Visual Studio project in the Plugin folder, go to project settings, under C/C++ find the additional include directories field and add the include folder that you copied, then build the Visual Studio project.
DO NOT build the Visual Studio project while the Unity project is open, this will cause the Editor to crash.  
Incidentally, the screenshots shown here are from the samples included in the SDK and can be found in media/Mite.

3.  Import Built DLL
  * Build the visual studio project, when finsihed this will automatically copy the integration DLL and shader to the Unity project
  * Or import the package found under the Packages folder this will copy a prebuilt DLL and shader meaning that you will not have to build anything manually. Usefull if you don't plan on modifying the underlying code.


### Hair Instance Component
![mite](doc/hair_instance.png)  
As a prerequisite all hair related data (.apx and .cso files) should be placed under Assets/StreamingAssets.
Currently when building the Visual Studio project the DLL and shader should be placed automatically.

"Load Hair Asset": Specify the asset file (.apx) for hair.
"Load Hair Shader": Specify the hair shader (.cso = compiled HLSL).
"Reload Hair Asset / Shader": If you have updated the .apx or .cso files during execution, press this button to make Unity use the updated versions.
"Set Textures": Makes the Hair Instance use the textures specified in the Textures foldout.
Root_bone: Specify the root bone of the object. If the object has a SkinnedMeshRenderer component then its Root_bone is specified by default.
Invert_bone_x: When checked the X coordinate of the bone will be inverted. There are cases where this setting is necessary to match the coordinate system specified in an FBX file.
Params: Set the simulation parameters. These parameters are also included in the .apx file, which is set by default. Use this for fine adjustment.

**Hair Scale**
With this fork of HairWorks it is no longer necessary to set the scale factor of the FBX to 100.
There is now a scale factor field in the Hair Instance script that will allow you to adjust scale manually should it be incorrect.
By default HairWorks will now attempt to match the normal size of the FBX in Unity.

### Hair Light Component
This component is required to light a Hair Instance. Can be added as a component to a point, spot, or directional light.
![mite](doc/hair_light.png)  
If you check copy_light_params then any modifications made to the Unity light will also be reflected in the Hair Light component.

### Hair Shadow
The Hair Light Component now has some parameters for shadow mapping.
Shadows can be enabled/disabled per light and their resolution can be set as well.
Supports soft shadows.
Currently shadows can only be received from a single directional light.
Point light and Spot light shadows coming soon.

## Warning
**The NVIDIA GameWorks SDK, including Hair Works, obligates you to display the NVIDIA logo when using it**
This also applies when using this plug-in. Please be sure to follow this requirement when using HairWorks.   
[GameWorks SDK EULA](https://developer.nvidia.com/gameworks-sdk-eula)  

Roughly summarized, it is necessary to display a NVIDIA Game Works logo on the game start screen, manuals, press releases, etc. In addition, in case of commercial use it is necessary to contact NVIDIA. There seems to be no particular kind of license fee.

## License
[MIT](HairWorksIntegration/Assets/StreamingAssets/UTJ/HairWorksIntegration/License.txt)
