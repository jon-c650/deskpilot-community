#define UNICODE
#define _UNICODE
#define _WIN32_WINNT 0x0601
#include <windows.h>
#include <wincrypt.h>
#include <wintrust.h>
#include <softpub.h>
#include <shellapi.h>
#include <string>
#include <vector>
#include <algorithm>
#include <cwctype>
#pragma comment(lib,"advapi32.lib")
#pragma comment(lib,"crypt32.lib")
#pragma comment(lib,"shell32.lib")
#pragma comment(lib,"delayimp.lib")
// Standalone, statically linked helper: no CLR, app config, plugins, JSON library
// or user-directory DLLs are loaded. Wintrust is loaded explicitly from System32.
using S=std::wstring;
struct Handle  {
    HANDLE h=INVALID_HANDLE_VALUE;
    Handle() {
    }
    explicit Handle(HANDLE value):h(value) {
    }
    ~Handle() {
        if(h!=INVALID_HANDLE_VALUE && h) CloseHandle(h);
    }
    operator HANDLE()const {
        return h;
    }
}
;
struct Failure  {
    S text;
}
;
static void fail(const wchar_t* text) {
    throw Failure {
        text
    }
    ;
}
static bool validName(const S& name) {
    if(name.empty() || name.size()>32 || name==L"." || name==L"..")return false;
    return std::all_of(name.begin(),name.end(),[](wchar_t c) {
        return c<128 && (iswalnum(c) || S(L"_=+.-").find(c)!=S::npos);
    }
    );
}
static S trim(S text) {
    while(!text.empty() && iswspace(text.back()))text.pop_back();
    auto i=text.find_first_not_of(L" \t\r\n\xfeff");
    return i==S::npos ? L"" : text.substr(i);
}
static S lower(S text) {
    for(auto& c:text)c=(wchar_t)towlower(c);
    return text;
}
static bool exists(const S& path) {
    return GetFileAttributesW(path.c_str())!=INVALID_FILE_ATTRIBUTES;
}
static void readAll(HANDLE h,void* p,DWORD size) {
    DWORD offset=0,n=0;
    while(offset<size) {
        if(!ReadFile(h,(BYTE*)p+offset,size-offset,&n,nullptr)||n==0)fail(L"The profile request was incomplete.");
        offset+=n;
    }
}
static void writeAll(HANDLE h,const void* p,DWORD size) {
    DWORD offset=0,n=0;
    while(offset<size) {
        if(!WriteFile(h,(BYTE*)p+offset,size-offset,&n,nullptr)||n==0)fail(L"The profile response could not be delivered.");
        offset+=n;
    }
}
static S decode(const std::vector<BYTE>& bytes) {
    if(bytes.empty())return L"";
    int count=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,(LPCCH)bytes.data(),(int)bytes.size(),nullptr,0);
    if(count<=0)fail(L"Unsupported configuration encoding.");
    S text(count,L'\0');
    MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,(LPCCH)bytes.data(),(int)bytes.size(),&text[0],count);
    return text;
}
static std::vector<BYTE> utf8(const S& text) {
    if(text.empty())return  {
    }
    ;
    int count=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text.data(),(int)text.size(),nullptr,0,nullptr,nullptr);
    if(count<=0)fail(L"Invalid profile message.");
    std::vector<BYTE> bytes(count);
    WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text.data(),(int)text.size(),(LPSTR)bytes.data(),count,nullptr,nullptr);
    return bytes;
}
static S readText(HANDLE pipe,DWORD maximum) {
    DWORD size;
    readAll(pipe,&size,4);
    if(size>maximum)fail(L"Profile request exceeds its limit.");
    std::vector<BYTE> bytes(size);
    if(size)readAll(pipe,bytes.data(),size);
    return decode(bytes);
}
static void writeText(HANDLE pipe,const S& text) {
    auto bytes=utf8(text);
    DWORD size=(DWORD)bytes.size();
    writeAll(pipe,&size,4);
    if(size)writeAll(pipe,bytes.data(),size);
}
static std::vector<BYTE> readFile(const S& path) {
    Handle h(CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));
    if(h.h==INVALID_HANDLE_VALUE)fail(L"The selected configuration cannot be read.");
    LARGE_INTEGER size;
    if(!GetFileSizeEx(h,&size)||size.QuadPart<=0||size.QuadPart>1024*1024)fail(L"The configuration is empty or exceeds the 1 MB limit.");
    std::vector<BYTE> data((size_t)size.QuadPart);
    readAll(h,data.data(),(DWORD)data.size());
    return data;
}
static S sourcePath(const S& source) {
    if(source.size()<8 || source.size()>2048 || !iswalpha(source[0]) || source[1]!=L':' || source[2]!=L'\\' || source.find(L'\0')!=S::npos)fail(L"Choose a normal local .conf file.");
    wchar_t full[32768];
    DWORD length=GetFullPathNameW(source.c_str(),32768,full,nullptr);
    if(!length || length>=32768)fail(L"Invalid source path.");
    S path(full);
    if(lower(path.substr(path.size()-5))!=L".conf")fail(L"Only .conf files can be imported.");
    S drive=path.substr(0,3);
    if(GetDriveTypeW(drive.c_str())==DRIVE_REMOTE)fail(L"Copy the configuration to a local drive first.");
    for(S p=path;p.size()>=3;) {
        DWORD attributes=GetFileAttributesW(p.c_str());
        if(attributes==INVALID_FILE_ATTRIBUTES || (attributes&FILE_ATTRIBUTE_REPARSE_POINT))fail(L"Linked or inaccessible source paths cannot be imported.");
        if(p.size()==3)break;
        size_t slash=p.find_last_of(L'\\');
        p=p.substr(0,slash==2 ? 3 : slash);
    }
    return path;
}
static bool keyValid(const S& text) {
    DWORD size=0;
    return CryptStringToBinaryW(text.c_str(),(DWORD)text.size(),CRYPT_STRING_BASE64,nullptr,&size,nullptr,nullptr) && size==32;
}
static void validate(const std::vector<BYTE>& data) {
    S text;
    if(data.size()>=2 && ((data[0]==255&&data[1]==254)||(data[0]==254&&data[1]==255))) {
        if(data.size()%2)fail(L"Invalid configuration encoding.");
        bool big=data[0]==254;
        for(size_t i=2;i<data.size();i+=2)text.push_back((wchar_t)(big ? (data[i]<<8)|data[i+1] : data[i]|(data[i+1]<<8)));
    }
    else text=decode(data);
    int section=0,peers=0;
    bool interfaceSeen=false,privateKey=false,peerKey=false;
    size_t start=0;
    while(start<=text.size()) {
        size_t end=text.find(L'\n',start);
        S line=trim(text.substr(start,end==S::npos?S::npos:end-start));
        size_t comment=line.find(L'#');
        if(comment!=S::npos)line=trim(line.substr(0,comment));
        start=end==S::npos ? text.size()+1 : end+1;
        if(line.empty())continue;
        S folded=lower(line);
        if(folded==L"[interface]") {
            if(interfaceSeen)fail(L"Duplicate Interface section.");
            interfaceSeen=true;
            section=1;
            continue;
        }
        if(folded==L"[peer]") {
            if(section==2&&!peerKey)fail(L"A peer is missing its public key.");
            section=2;
            peers++;
            peerKey=false;
            continue;
        }
        size_t equal=line.find(L'=');
        if(equal==S::npos || section==0)fail(L"Invalid WireGuard configuration.");
        S key=lower(trim(line.substr(0,equal))),value=trim(line.substr(equal+1));
        if(key==L"preup"||key==L"postup"||key==L"predown"||key==L"postdown")fail(L"Configuration command hooks are not accepted.");
        if(value.empty())fail(L"A configuration setting is empty.");
        S allowed=section==1?L"|privatekey|address|dns|listenport|mtu|table|":L"|publickey|presharedkey|allowedips|endpoint|persistentkeepalive|";
        if(allowed.find(L"|"+key+L"|")==S::npos)fail(L"Unsupported configuration setting.");
        if(key==L"privatekey"||key==L"publickey"||key==L"presharedkey") {
            if(!keyValid(value))fail(L"Invalid configuration key.");
            if(key==L"privatekey")privateKey=true;
            if(key==L"publickey")peerKey=true;
        }
    }
    if(!text.empty())SecureZeroMemory(&text[0],text.size()*sizeof(wchar_t));
    if(!interfaceSeen||!privateKey||peers==0||section!=2||!peerKey)fail(L"An Interface, private key and keyed Peer are required.");
}
static S installation() {
    wchar_t value[32768];
    DWORD size=sizeof(value),type=0;
    HKEY key=nullptr;
    if(RegOpenKeyExW(HKEY_LOCAL_MACHINE,L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion",0,KEY_READ|KEY_WOW64_64KEY,&key)!=ERROR_SUCCESS)fail(L"Windows Program Files location is unavailable.");
    LONG result=RegQueryValueExW(key,L"ProgramFilesDir",nullptr,&type,(BYTE*)value,&size);
    RegCloseKey(key);
    if(result!=ERROR_SUCCESS||type!=REG_SZ||size>=sizeof(value))fail(L"Windows Program Files location is unavailable.");
    value[size/sizeof(wchar_t)]=0;
    return S(value)+L"\\WireGuard";
}
static void verifyOfficial(const S& exe) {
    wchar_t system[MAX_PATH];
    if(!GetSystemDirectoryW(system,MAX_PATH))fail(L"Windows signature verification is unavailable.");
    S path=S(system)+L"\\wintrust.dll";
    HMODULE trust=LoadLibraryExW(path.c_str(),nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!trust)fail(L"Windows signature verification is unavailable.");
    auto verify=(LONG(WINAPI*)(HWND,GUID*,LPVOID))GetProcAddress(trust,"WinVerifyTrust");
    WINTRUST_FILE_INFO file {
    }
    ;
    file.cbStruct=sizeof(file);
    file.pcwszFilePath=exe.c_str();
    WINTRUST_DATA data {
    }
    ;
    data.cbStruct=sizeof(data);
    data.dwUIChoice=WTD_UI_NONE;
    data.dwUnionChoice=WTD_CHOICE_FILE;
    data.pFile=&file;
    data.dwStateAction=WTD_STATEACTION_VERIFY;
    data.dwProvFlags=WTD_CACHE_ONLY_URL_RETRIEVAL;
    GUID action=WINTRUST_ACTION_GENERIC_VERIFY_V2;
    LONG status=verify ? verify((HWND)INVALID_HANDLE_VALUE,&action,&data) : ERROR_INVALID_FUNCTION;
    data.dwStateAction=WTD_STATEACTION_CLOSE;
    if(verify)verify((HWND)INVALID_HANDLE_VALUE,&action,&data);
    FreeLibrary(trust);
    if(status!=ERROR_SUCCESS)fail(L"The installed WireGuard signature could not be verified.");
    HCERTSTORE store=nullptr;
    HCRYPTMSG message=nullptr;
    if(!CryptQueryObject(CERT_QUERY_OBJECT_FILE,exe.c_str(),CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED,CERT_QUERY_FORMAT_FLAG_BINARY,0,nullptr,nullptr,nullptr,&store,&message,nullptr))fail(L"WireGuard signer is unavailable.");
    DWORD length=0;
    PCCERT_CONTEXT cert=nullptr;
    bool official=false;
    if(CryptMsgGetParam(message,CMSG_SIGNER_INFO_PARAM,0,nullptr,&length)) {
        std::vector<BYTE> buffer(length);
        if(CryptMsgGetParam(message,CMSG_SIGNER_INFO_PARAM,0,buffer.data(),&length)) {
            auto signer=(CMSG_SIGNER_INFO*)buffer.data();
            CERT_INFO info {
            }
            ;
            info.Issuer=signer->Issuer;
            info.SerialNumber=signer->SerialNumber;
            cert=CertFindCertificateInStore(store,X509_ASN_ENCODING|PKCS_7_ASN_ENCODING,0,CERT_FIND_SUBJECT_CERT,&info,nullptr);
            if(cert) {
                wchar_t name[256] {
                }
                ;
                CertGetNameStringW(cert,CERT_NAME_SIMPLE_DISPLAY_TYPE,0,nullptr,name,256);
                official=_wcsicmp(name,L"WireGuard LLC")==0;
            }
        }
    }
    if(cert)CertFreeCertificateContext(cert);
    CryptMsgClose(message);
    CertCloseStore(store,0);
    if(!official)fail(L"The installed executable is not signed by WireGuard LLC.");
}
struct Service  {
    SC_HANDLE h=nullptr;
    explicit Service(SC_HANDLE value):h(value) {
    }
    ~Service() {
        if(h)CloseServiceHandle(h);
    }
    operator SC_HANDLE()const {
        return h;
    }
}
;
static bool serviceExists(const S& name) {
    Service scm(OpenSCManagerW(nullptr,nullptr,SC_MANAGER_CONNECT));
    if(!scm.h)fail(L"Windows service status is unavailable.");
    Service service(OpenServiceW(scm,name.c_str(),SERVICE_QUERY_STATUS));
    if(service.h)return true;
    if(GetLastError()!=ERROR_SERVICE_DOES_NOT_EXIST)fail(L"Windows service status is unavailable.");
    return false;
}
static void manager(const S& directory) {
    Service scm(OpenSCManagerW(nullptr,nullptr,SC_MANAGER_CONNECT));
    if(!scm.h)fail(L"WireGuard Manager is unavailable.");
    Service service(OpenServiceW(scm,L"WireGuardManager",SERVICE_QUERY_STATUS|SERVICE_START));
    if(!service.h) {
        if(GetLastError()!=ERROR_SERVICE_DOES_NOT_EXIST)fail(L"WireGuard Manager access was denied.");
        S exe=directory+L"\\wireguard.exe",args=L"\""+exe+L"\" /installmanagerservice";
        STARTUPINFOW startup {
        }
        ;
        startup.cb=sizeof(startup);
        PROCESS_INFORMATION process {
        }
        ;
        if(!CreateProcessW(exe.c_str(),&args[0],nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,directory.c_str(),&startup,&process))fail(L"Official WireGuard Manager could not start.");
        Handle thread(process.hThread),handle(process.hProcess);
        DWORD code=1;
        if(WaitForSingleObject(handle,60000)!=WAIT_OBJECT_0 || !GetExitCodeProcess(handle,&code) || code!=0)fail(L"WireGuard Manager startup has not been confirmed.");
        service.h=OpenServiceW(scm,L"WireGuardManager",SERVICE_QUERY_STATUS|SERVICE_START);
        if(!service.h)fail(L"WireGuard Manager is unavailable.");
    }
    SERVICE_STATUS status {
    }
    ;
    if(!QueryServiceStatus(service,&status))fail(L"WireGuard Manager status is unavailable.");
    if(status.dwCurrentState==SERVICE_STOPPED && !StartServiceW(service,0,nullptr))fail(L"WireGuard Manager could not start.");
    ULONGLONG end=GetTickCount64()+30000;
    do {
        if(QueryServiceStatus(service,&status)&&status.dwCurrentState==SERVICE_RUNNING)return;
        Sleep(100);
    }
    while(GetTickCount64()<end);
    fail(L"WireGuard Manager startup timed out.");
}
static std::vector<S> discover(const S& directory) {
    WIN32_FIND_DATAW item {
    }
    ;
    HANDLE find=FindFirstFileW((directory+L"\\*.conf.dpapi").c_str(),&item);
    std::vector<S> names;
    if(find==INVALID_HANDLE_VALUE) {
        if(GetLastError()==ERROR_FILE_NOT_FOUND)return names;
        fail(L"Protected WireGuard profile names could not be read.");
    }
    do {
        S file=item.cFileName;
        if(!(item.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY)&&file.size()>11) {
            S name=file.substr(0,file.size()-11);
            if(validName(name))names.push_back(name);
        }
        if(names.size()>=1000)break;
    }
    while(FindNextFileW(find,&item));
    FindClose(find);
    std::sort(names.begin(),names.end());
    return names;
}
static void importProfile(const S& directory,const S& name,std::vector<BYTE>& data) {
    if(!validName(name))fail(L"Invalid tunnel name.");
    DWORD attributes=GetFileAttributesW(directory.c_str());
    if(attributes==INVALID_FILE_ATTRIBUTES || (attributes&FILE_ATTRIBUTE_REPARSE_POINT))fail(L"The protected WireGuard directory is unavailable.");
    S plain=directory+L"\\"+name+L".conf",encrypted=plain+L".dpapi";
    if(exists(plain)||exists(encrypted)||serviceExists(L"WireGuardTunnel$"+name))fail(L"A WireGuard profile or service already uses this name. It was not replaced.");
    bool created=false;
    try {
        Handle file(CreateFileW(plain.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL|FILE_FLAG_WRITE_THROUGH,nullptr));
        if(file.h==INVALID_HANDLE_VALUE)fail(L"The profile could not be staged without replacing an existing file.");
        created=true;
        writeAll(file,data.data(),(DWORD)data.size());
        if(!FlushFileBuffers(file))fail(L"The profile could not be staged.");
    }
    catch(...) {
        if(created)DeleteFileW(plain.c_str());
        throw;
    }
    ULONGLONG end=GetTickCount64()+20000;
    do {
        if(exists(encrypted)&&!exists(plain))return;
        Sleep(100);
    }
    while(GetTickCount64()<end);
    try {
        if(exists(plain)&&readFile(plain)==data)DeleteFileW(plain.c_str());
    }
    catch(...) {
    }
    fail(L"WireGuard has not confirmed protection of the imported file. Check its official log before retrying. No tunnel was connected by DeskPilot.");
}
static int execute(int argc,wchar_t** argv) {
    if(argc!=3)return 1;
    S pipeName=argv[1];
    if(pipeName.substr(0,12)!=L"DeskPilotWG-" || pipeName.size()!=44 || !std::all_of(pipeName.begin()+12,pipeName.end(),[](wchar_t c) {
        return (c>=L'0'&&c<=L'9')||(c>=L'a'&&c<=L'f');
    }
    ))return 1;
    wchar_t* tail=nullptr;
    unsigned long parent=wcstoul(argv[2],&tail,10);
    if(!tail||*tail||!parent)return 1;
    S path=L"\\\\.\\pipe\\"+pipeName;
    if(!WaitNamedPipeW(path.c_str(),20000))return 1;
    Handle pipe(CreateFileW(path.c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,SECURITY_SQOS_PRESENT|SECURITY_IDENTIFICATION,nullptr));
    if(pipe.h==INVALID_HANDLE_VALUE)return 1;
    ULONG server=0;
    if(!GetNamedPipeServerProcessId(pipe,&server)||server!=parent)return 1;
    std::vector<S> names;
    S imported,error;
    int code=1;
    try {
#ifndef PROFILE_HELPER_PIPE_TESTS
        SID_IDENTIFIER_AUTHORITY nt=SECURITY_NT_AUTHORITY;
        PSID sid=nullptr;
        BOOL admin=FALSE;
        if(AllocateAndInitializeSid(&nt,2,SECURITY_BUILTIN_DOMAIN_RID,DOMAIN_ALIAS_RID_ADMINS,0,0,0,0,0,0,&sid)) {
            CheckTokenMembership(nullptr,sid,&admin);
            FreeSid(sid);
        }
        if(!admin)fail(L"Administrator approval is required.");
#endif
        BYTE version=0,operation=0;
        readAll(pipe,&version,1);
        readAll(pipe,&operation,1);
        // Consume the complete bounded request before replying. Rejecting an
        // operation between writes can deadlock a zero-buffer duplex pipe.
        S source=readText(pipe,8192);
        if(version!=1 || (operation!=1&&operation!=2))fail(L"Unsupported profile request.");
        S directory=installation();
        verifyOfficial(directory+L"\\wireguard.exe");
        if(operation==2) {
            source=sourcePath(source);
            auto data=readFile(source);
            try {
                validate(data);
                size_t slash=source.find_last_of(L'\\');
                imported=source.substr(slash+1,source.size()-slash-6);
                if(!validName(imported))fail(L"Invalid tunnel name.");
                manager(directory);
                importProfile(directory+L"\\Data\\Configurations",imported,data);
            }
            catch(...) {
                SecureZeroMemory(data.data(),data.size());
                throw;
            }
            SecureZeroMemory(data.data(),data.size());
        }
        names=discover(directory+L"\\Data\\Configurations");
        code=0;
    }
    catch(const Failure& failure) {
        error=failure.text;
        imported.clear();
        names.clear();
    }
    catch(...) {
        error=L"WireGuard import/discovery did not complete. No success is assumed.";
        imported.clear();
        names.clear();
    }
    BYTE version=1;
    writeAll(pipe,&version,1);
    DWORD count=(DWORD)names.size();
    writeAll(pipe,&count,4);
    for(const auto& name:names)writeText(pipe,name);
    writeText(pipe,imported);
    writeText(pipe,error);
    return code;
}
#ifdef PROFILE_HELPER_TESTS
int main() {
    if(!SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32))return 6;
    try  {
        if(!validName(L"Customer_1") || validName(L"../escape") || validName(L"bad name"))return 2;
        S config=L"[Interface]\nPrivateKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\n[Peer]\nPublicKey = AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE=\nAllowedIPs = 192.0.2.0/24\n";
        auto bytes=utf8(config);
        validate(bytes);
        for(const auto& hook: {
            L"PreUp",L"PostUp",L"PreDown",L"PostDown"
        }
        ) {
            bool blocked=false;
            try {
                validate(utf8(config+S(hook)+L" = not-executed\n"));
            }
            catch(const Failure&) {
                blocked=true;
            }
            if(!blocked)return 3;
        }
        bool malformed=false;
        try {
            validate(utf8(L"[Interface]\nPrivateKey = bad\n[Peer]\n"));
        }
        catch(const Failure&) {
            malformed=true;
        }
        if(!malformed)return 4;
        return 0;
    }
    catch(...) {
        return 5;
    }
}
#else
int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR,int) {
    if(!SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32))return 1;
    int argc=0;
    LPWSTR* argv=CommandLineToArgvW(GetCommandLineW(),&argc);
    if(!argv)return 1;
    int result=1;
    try {
        result=execute(argc,argv);
    }
    catch(...) {
    }
    LocalFree(argv);
    return result;
}
#endif
