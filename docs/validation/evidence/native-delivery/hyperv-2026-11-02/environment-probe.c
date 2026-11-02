#include <windows.h>
static BOOL exists(LPCWSTR path){return GetFileAttributesW(path)!=INVALID_FILE_ATTRIBUTES;}
void main(void){
 WCHAR path[MAX_PATH],user[MAX_PATH],local[MAX_PATH],target[MAX_PATH];
 GetEnvironmentVariableW(L"USERPROFILE",user,MAX_PATH);lstrcatW(user,L"\\.dotnet");
 GetEnvironmentVariableW(L"LOCALAPPDATA",local,MAX_PATH);lstrcatW(local,L"\\Microsoft\\dotnet");
 BOOL onPath=SearchPathW(NULL,L"dotnet.exe",NULL,MAX_PATH,path,NULL)>0;
 HKEY key;BOOL installed=RegOpenKeyExW(HKEY_LOCAL_MACHINE,L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64",0,KEY_READ|KEY_WOW64_64KEY,&key)==ERROR_SUCCESS;if(installed)RegCloseKey(key);
 GetModuleFileNameW(NULL,target,MAX_PATH);int i=lstrlenW(target);while(i>0&&target[i-1]!=L'\\')--i;target[i]=0;lstrcatW(target,L"environment.json");
 SYSTEM_INFO system;GetNativeSystemInfo(&system);CHAR text[1024];DWORD written;
 int count=wsprintfA(text,"{\"dotnetOnPath\":%s,\"programFilesDotnet\":%s,\"programFilesX86Dotnet\":%s,\"userDotnet\":%s,\"localDotnet\":%s,\"dotnetInstalledRegistry\":%s,\"processorArchitecture\":%u,\"probe\":\"native Win32; no managed runtime dependency\"}\r\n",onPath?"true":"false",exists(L"C:\\Program Files\\dotnet")?"true":"false",exists(L"C:\\Program Files (x86)\\dotnet")?"true":"false",exists(user)?"true":"false",exists(local)?"true":"false",installed?"true":"false",system.wProcessorArchitecture);
 HANDLE file=CreateFileW(target,GENERIC_WRITE,0,NULL,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,NULL);if(file==INVALID_HANDLE_VALUE)ExitProcess(1);WriteFile(file,text,count,&written,NULL);CloseHandle(file);ExitProcess(written==count?0:2);
}
