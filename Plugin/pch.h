#include <algorithm>
#include <map>
#include <vector>
#include <memory>
#include <functional>
#include <fstream>
#include <cstdint>
#include <array>
#include <thread>
#include <mutex>

#include <d3d11.h>
#include <directXMath.h>

#ifndef NDEBUG
#define NDEBUG
#endif // !NDEBUG


#include <Nv\HairWorks\NvHairSdk.h>
#include <Nv\HairWorks\NvHairCommon.h>
#include <Nv\Core\1.0\NvAssert.h>
#include <Nv\Core\1.0\NvDefines.h>
#include <Nv\Core\1.0\NvResult.h>
#include <Nv\Core\1.0\NvTypes.h>
#include <Nv\HairWorks\Platform\Win\NvHairWinLoadSdk.h>
#include <Nv\Common\NvCoMemoryAllocator.h>
#include <Nv\Common\NvCoLogger.h>
#include <Nv\Common\Platform\Dx11\NvCoDx11Handle.h>
#include <Nv\Common\Platform\StdC\NvCoStdCFileReadStream.h>
#include <IUnityGraphics.h>
#include <IUnityGraphicsD3D11.h>
