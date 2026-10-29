// Isolated Vulkan loader adapter. Every call is forwarded to the Windows
// loader; one explicitly whitelisted fragment shader may receive a UI-only
// texture-coordinate transform. No game files or world sampling are changed.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstddef>
#include <cstring>
#include <cstdio>
#include <cstdlib>
#include <vector>
#include <string>
#include <mutex>
#include <cmath>

using Instance=void*;using Device=void*;using Shader=uint64_t;
using Proc=void (WINAPI*)();
using GIPA=Proc(WINAPI*)(Instance,const char*);
using GDPA=Proc(WINAPI*)(Device,const char*);
struct ShaderInfo {uint32_t type;const void* next;uint32_t flags;size_t size;const uint32_t* code;};
using CreateShader=int32_t(WINAPI*)(Device,const ShaderInfo*,const void*,Shader*);
static HMODULE selfModule;
static HMODULE realModule;
static GIPA realGipa;
static GDPA realGdpa;
static std::once_flag initFlag;
static std::mutex logMutex;
static std::wstring directory;
static void logLine(const char* message,uint64_t hash,size_t words);

static void initialise() {
 std::call_once(initFlag,[]{
  wchar_t path[32768];GetModuleFileNameW(selfModule,path,32768);
  directory=path;directory.resize(directory.find_last_of(L"\\/")+1);
  wchar_t system[32768];GetSystemDirectoryW(system,32768);
  std::wstring loader=std::wstring(system)+L"\\vulkan-1.dll";
  realModule=LoadLibraryW(loader.c_str());
  if(realModule){realGipa=reinterpret_cast<GIPA>(GetProcAddress(realModule,"vkGetInstanceProcAddr"));
                 realGdpa=reinterpret_cast<GDPA>(GetProcAddress(realModule,"vkGetDeviceProcAddr"));}
  logLine(realGipa&&realGdpa?"adapter loaded; Windows loader ready":"Windows loader unavailable",0,0);
 });
}
static void logLine(const char* message,uint64_t hash,size_t words) {
 std::lock_guard<std::mutex> lock(logMutex);wchar_t name[80];
 swprintf_s(name,L"ui-proxy-%lu.log",GetCurrentProcessId());FILE* f=nullptr;
 _wfopen_s(&f,(directory+name).c_str(),L"a");
 if(f){fprintf(f,"%s hash=%016llx words=%zu\n",message,static_cast<unsigned long long>(hash),words);fclose(f);}
}
static uint64_t hashCode(const uint32_t* p,size_t words) {
 uint64_t h=14695981039346656037ull;auto bytes=reinterpret_cast<const uint8_t*>(p);
 for(size_t i=0;i<words*4;i++){h^=bytes[i];h*=1099511628211ull;}return h;
}
struct Analysis {uint32_t scalar=0,vec2=0;size_t sample=0,function=0;int implicit=0,explicitLod=0;bool fragment=false;};
static bool analyse(const uint32_t* p,size_t n,Analysis& a) {
 if(n<5||p[0]!=0x07230203||p[3]>10000000)return false;
 for(size_t i=5;i<n;){uint32_t wc=p[i]>>16,op=p[i]&65535;if(!wc||i+wc>n)return false;
  if(op==15&&wc>=4&&p[i+1]==4)a.fragment=true;
  if(op==22&&wc==3&&p[i+2]==32)a.scalar=p[i+1];
  if(op==54&&!a.function)a.function=i;
  if(op==87){a.implicit++;a.sample=i;}
  if(op==88)a.explicitLod++;
  i+=wc;
 }
 for(size_t i=5;i<n;i+=p[i]>>16)
  if((p[i]&65535)==23&&(p[i]>>16)==4&&p[i+2]==a.scalar&&p[i+3]==2)a.vec2=p[i+1];
 return a.fragment&&a.scalar&&a.vec2&&a.function&&a.implicit==1;
}
static uint32_t bits(float v){uint32_t b;memcpy(&b,&v,4);return b;}
static void emit(std::vector<uint32_t>& v,std::initializer_list<uint32_t> w){v.insert(v.end(),w);}
static bool transform(const uint32_t* p,size_t n,const Analysis& a,float dx,float dy,float scale,std::vector<uint32_t>& out){
 if(!std::isfinite(dx)||!std::isfinite(dy)||!std::isfinite(scale)||scale<.25f||scale>2.f)return false;
 auto sample=a.sample;if((p[sample]>>16)<5)return false;
 uint32_t coordinate=p[sample+4];bool typed=false;
 for(size_t i=a.function;i<sample;i+=p[i]>>16)
  if((p[i]>>16)>=3&&p[i+1]==a.vec2&&p[i+2]==coordinate)typed=true;
 if(!typed)return false;
 uint32_t next=p[3],x=next++,y=next++,delta=next++,sc=next++,scv=next++,shifted=next++,scaled=next++;
 out.assign(p,p+5);out[3]=next;
 for(size_t i=5;i<n;){uint32_t wc=p[i]>>16;
  if(i==a.function){
   emit(out,{(4u<<16)|43,a.scalar,x,bits(dx)});emit(out,{(4u<<16)|43,a.scalar,y,bits(dy)});
   emit(out,{(5u<<16)|44,a.vec2,delta,x,y});
   emit(out,{(4u<<16)|43,a.scalar,sc,bits(scale)});emit(out,{(5u<<16)|44,a.vec2,scv,sc,sc});
  }
  if(i==sample){
   emit(out,{(5u<<16)|131,a.vec2,shifted,coordinate,delta});
   emit(out,{(5u<<16)|136,a.vec2,scaled,shifted,scv});
   size_t start=out.size();out.insert(out.end(),p+i,p+i+wc);out[start+4]=scaled;
  }else out.insert(out.end(),p+i,p+i+wc);
  i+=wc;
 }
 return true;
}
static void dump(const uint32_t* p,size_t n,uint64_t hash,const wchar_t* suffix){
 wchar_t name[100];swprintf_s(name,L"shader-%016llx-%s.spv",static_cast<unsigned long long>(hash),suffix);
 FILE* f=nullptr;_wfopen_s(&f,(directory+name).c_str(),L"wb");if(f){fwrite(p,4,n,f);fclose(f);}
}
static int32_t WINAPI createShader(Device dev,const ShaderInfo* info,const void* alloc,Shader* shader){
 initialise();auto real=reinterpret_cast<CreateShader>(realGdpa(dev,"vkCreateShaderModule"));
 if(!real)return -3;
 Analysis a;size_t n=info->size/4;
 if(info->size%4||!analyse(info->code,n,a))return real(dev,info,alloc,shader);
 uint64_t hash=hashCode(info->code,n);char value[128]={};
 GetEnvironmentVariableA("CODEX_UI_SHADER_HASH",value,sizeof(value));
 bool selected=false;char* cursor=value;
 while(*cursor){char* end=nullptr;uint64_t allowed=strtoull(cursor,&end,16);if(end==cursor)break;
  if(allowed==hash)selected=true;cursor=end;while(*cursor==','||*cursor==' '||*cursor==';')cursor++;
 }
 if(a.explicitLod==5){dump(info->code,n,hash,L"original");logLine("composite candidate",hash,n);}
 if(!selected)return real(dev,info,alloc,shader);
 float scale=1.f;value[0]=0;GetEnvironmentVariableA("CODEX_UI_SCALE",value,sizeof(value));if(*value)scale=strtof(value,nullptr);
 float output=5160.f;value[0]=0;GetEnvironmentVariableA("CODEX_UI_OUTPUT_WIDTH",value,sizeof(value));if(*value)output=strtof(value,nullptr);
 if(!std::isfinite(output)||output<3840.f||output>16384.f)output=5160.f;
 float dx=(1.f-(3840.f/output)*scale)*.5f,dy=(1.f-scale)*.5f;
 std::vector<uint32_t> modified;
 if(!transform(info->code,n,a,dx,dy,scale,modified)){logLine("rejected invalid transform",hash,n);return real(dev,info,alloc,shader);}
 ShaderInfo changed=*info;changed.code=modified.data();changed.size=modified.size()*4;
 int32_t result=real(dev,&changed,alloc,shader);
 if(result==0){dump(modified.data(),modified.size(),hash,L"centered");logLine("UI composite transformed",hash,modified.size());}
 else logLine("modified shader creation failed",hash,modified.size());
 return result;
}
extern "C" __declspec(dllexport) Proc WINAPI vkGetDeviceProcAddr(Device dev,const char* name);
extern "C" __declspec(dllexport) Proc WINAPI vkGetInstanceProcAddr(Instance inst,const char* name){
 initialise();if(!realGipa)return nullptr;
 if(!strcmp(name,"vkGetInstanceProcAddr"))return reinterpret_cast<Proc>(&vkGetInstanceProcAddr);
 if(!strcmp(name,"vkGetDeviceProcAddr"))return reinterpret_cast<Proc>(&vkGetDeviceProcAddr);
 if(!strcmp(name,"vkCreateShaderModule"))return reinterpret_cast<Proc>(&createShader);
 return realGipa(inst,name);
}
extern "C" __declspec(dllexport) Proc WINAPI vkGetDeviceProcAddr(Device dev,const char* name){
 initialise();if(!realGdpa)return nullptr;
 if(!strcmp(name,"vkGetDeviceProcAddr"))return reinterpret_cast<Proc>(&vkGetDeviceProcAddr);
 if(!strcmp(name,"vkCreateShaderModule"))return reinterpret_cast<Proc>(&createShader);
 return realGdpa(dev,name);
}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){selfModule=module;DisableThreadLibraryCalls(module);}return TRUE;}
#ifdef UI_PROXY_TEST
int main(int argc,char** argv){
 if(argc!=3)return 2;FILE* f=nullptr;fopen_s(&f,argv[1],"rb");if(!f)return 3;
 fseek(f,0,SEEK_END);long size=ftell(f);rewind(f);if(size<20||size%4){fclose(f);return 4;}
 std::vector<uint32_t> original(size/4);fread(original.data(),1,size,f);fclose(f);
 Analysis a;if(!analyse(original.data(),original.size(),a))return 5;
 std::vector<uint32_t> changed;if(!transform(original.data(),original.size(),a,660.f/5160.f,0.f,1.f,changed))return 6;
 fopen_s(&f,argv[2],"wb");if(!f)return 7;fwrite(changed.data(),4,changed.size(),f);fclose(f);
 printf("hash=%016llx implicit=%d explicit=%d original=%zu modified=%zu\n",static_cast<unsigned long long>(hashCode(original.data(),original.size())),a.implicit,a.explicitLod,original.size(),changed.size());return 0;
}
#endif
