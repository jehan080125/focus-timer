#include <windows.h>
#include <roapi.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/Windows.Security.Authorization.AppCapabilityAccess.h>
#include <vector>
#include <algorithm>
#include <cstring>
#include "GlassVS.h"
#include "GlassPS.h"

using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
using namespace winrt::Windows::Security::Authorization::AppCapabilityAccess;
using ::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess;

struct Output {
    HMONITOR monitor{}; RECT bounds{};
    GraphicsCaptureItem item{nullptr};
    Direct3D11CaptureFramePool pool{nullptr};
    GraphicsCaptureSession session{nullptr};
    com_ptr<ID3D11Texture2D> latest;
    ~Output() { try { if(session) session.Close(); if(pool) pool.Close(); } catch(...) {} }
};
struct Renderer {
    com_ptr<ID3D11Device> device;
    com_ptr<ID3D11DeviceContext> context;
    IDirect3DDevice winrtDevice{nullptr};
    com_ptr<ID3D11VertexShader> vs;
    com_ptr<ID3D11PixelShader> ps;
    com_ptr<ID3D11Buffer> constants;
    com_ptr<ID3D11SamplerState> sampler;
    com_ptr<ID3D11Texture2D> atlas, target, staging;
    com_ptr<ID3D11ShaderResourceView> srv;
    com_ptr<ID3D11RenderTargetView> rtv;
    std::vector<std::unique_ptr<Output>> outputs;
    int width{}, height{}, padding{32};
    int test{};
    Renderer(int synthetic) : test(synthetic) {
        check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, device.put(), nullptr, context.put()));
        com_ptr<IDXGIDevice> dxgi = device.as<IDXGIDevice>();
        com_ptr<IInspectable> inspectable;
        check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(), inspectable.put()));
        winrtDevice = inspectable.as<IDirect3DDevice>();
        check_hresult(device->CreateVertexShader(GlassVS, sizeof GlassVS, nullptr, vs.put()));
        check_hresult(device->CreatePixelShader(GlassPS, sizeof GlassPS, nullptr, ps.put()));
        D3D11_BUFFER_DESC buffer{}; buffer.ByteWidth = 32; buffer.Usage = D3D11_USAGE_DEFAULT; buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        check_hresult(device->CreateBuffer(&buffer, nullptr, constants.put()));
        D3D11_SAMPLER_DESC sd{}; sd.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sd.AddressU = sd.AddressV = sd.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP; sd.MaxLOD = D3D11_FLOAT32_MAX;
        check_hresult(device->CreateSamplerState(&sd, sampler.put()));
    }
    void Resize(int w, int h) {
        if(w == width && h == height) return;
        width = w; height = h;
        srv = nullptr; rtv = nullptr; atlas = nullptr; target = nullptr; staging = nullptr;
        D3D11_TEXTURE2D_DESC d{}; d.Width = w + padding * 2; d.Height = h + padding * 2;
        d.MipLevels = d.ArraySize = 1; d.Format = DXGI_FORMAT_B8G8R8A8_UNORM; d.SampleDesc.Count = 1;
        d.Usage = D3D11_USAGE_DEFAULT; d.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        check_hresult(device->CreateTexture2D(&d, nullptr, atlas.put()));
        check_hresult(device->CreateShaderResourceView(atlas.get(), nullptr, srv.put()));
        d.Width = w; d.Height = h; d.BindFlags = D3D11_BIND_RENDER_TARGET;
        check_hresult(device->CreateTexture2D(&d, nullptr, target.put()));
        check_hresult(device->CreateRenderTargetView(target.get(), nullptr, rtv.put()));
        d.Usage = D3D11_USAGE_STAGING; d.BindFlags = 0; d.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        check_hresult(device->CreateTexture2D(&d, nullptr, staging.put()));
    }
    bool Capture(RECT area) {
        std::vector<HMONITOR> monitors;
        EnumDisplayMonitors(nullptr, &area, [](HMONITOR m,HDC,LPRECT,LPARAM p)->BOOL {
            reinterpret_cast<std::vector<HMONITOR>*>(p)->push_back(m); return TRUE;
        }, reinterpret_cast<LPARAM>(&monitors));
        outputs.erase(std::remove_if(outputs.begin(), outputs.end(), [&](auto& o) {
            return std::find(monitors.begin(),monitors.end(),o->monitor) == monitors.end();
        }), outputs.end());
        bool ready = !monitors.empty();
        for(auto monitor : monitors) {
            auto it = std::find_if(outputs.begin(),outputs.end(),[&](auto& o){return o->monitor == monitor;});
            if(it == outputs.end()) {
                auto o = std::make_unique<Output>(); o->monitor = monitor;
                MONITORINFO info{sizeof info}; if(!GetMonitorInfo(monitor,&info)) throw_hresult(E_FAIL);
                o->bounds = info.rcMonitor;
                auto interop = get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
                check_hresult(interop->CreateForMonitor(monitor, guid_of<GraphicsCaptureItem>(), put_abi(o->item)));
                o->pool = Direct3D11CaptureFramePool::CreateFreeThreaded(winrtDevice, DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, o->item.Size());
                o->session = o->pool.CreateCaptureSession(o->item);
                o->session.IsCursorCaptureEnabled(false);
                o->session.IsBorderRequired(false);
                o->session.StartCapture();
                outputs.push_back(std::move(o)); it = outputs.end()-1;
            }
            auto& o = **it;
            MONITORINFO currentInfo{sizeof currentInfo};
            if(!GetMonitorInfo(monitor,&currentInfo) || !EqualRect(&currentInfo.rcMonitor,&o.bounds)) throw_hresult(DXGI_ERROR_ACCESS_LOST);
            if(auto frame = o.pool.TryGetNextFrame()) {
                auto access = frame.Surface().as<IDirect3DDxgiInterfaceAccess>();
                com_ptr<ID3D11Texture2D> texture;
                check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), texture.put_void()));
                D3D11_TEXTURE2D_DESC d{}; texture->GetDesc(&d);
                if(frame.ContentSize().Width != static_cast<int>(d.Width) || frame.ContentSize().Height != static_cast<int>(d.Height)) {
                    auto newSize = frame.ContentSize(); frame.Close(); o.latest = nullptr;
                    o.pool.Recreate(winrtDevice,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,newSize);
                    return false;
                }
                if(!o.latest) { d.Usage = D3D11_USAGE_DEFAULT; d.BindFlags = d.CPUAccessFlags = d.MiscFlags = 0; check_hresult(device->CreateTexture2D(&d,nullptr,o.latest.put())); }
                context->CopyResource(o.latest.get(),texture.get());
            }
            if(!o.latest) { ready = false; continue; }
            RECT overlap{};
            if(IntersectRect(&overlap,&area,&o.bounds)) {
                D3D11_TEXTURE2D_DESC d{}; o.latest->GetDesc(&d);
                D3D11_BOX box{static_cast<UINT>(overlap.left-o.bounds.left),static_cast<UINT>(overlap.top-o.bounds.top),0,
                    static_cast<UINT>(overlap.right-o.bounds.left),static_cast<UINT>(overlap.bottom-o.bounds.top),1};
                if(box.right > d.Width || box.bottom > d.Height) throw_hresult(DXGI_ERROR_ACCESS_LOST);
                context->CopySubresourceRegion(atlas.get(),0,overlap.left-area.left,overlap.top-area.top,0,o.latest.get(),0,&box);
            }
        }
        return ready;
    }
    void Draw(float dark, float dpi, unsigned char* pixels, int stride) {
        float values[8]{float(width),float(height),float(padding),dark,dpi};
        context->UpdateSubresource(constants.get(),0,nullptr,values,0,0);
        ID3D11Buffer* cb = constants.get(); context->PSSetConstantBuffers(0,1,&cb);
        ID3D11ShaderResourceView* view = srv.get(); context->PSSetShaderResources(0,1,&view);
        ID3D11SamplerState* s = sampler.get(); context->PSSetSamplers(0,1,&s);
        ID3D11RenderTargetView* renderTarget = rtv.get(); context->OMSetRenderTargets(1,&renderTarget,nullptr);
        D3D11_VIEWPORT viewport{0,0,float(width),float(height),0,1}; context->RSSetViewports(1,&viewport);
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vs.get(),nullptr,0); context->PSSetShader(ps.get(),nullptr,0);
        context->Draw(3,0);
        view = nullptr; context->PSSetShaderResources(0,1,&view);
        context->OMSetRenderTargets(0,nullptr,nullptr);
        context->CopyResource(staging.get(),target.get());
        D3D11_MAPPED_SUBRESOURCE mapped{}; check_hresult(context->Map(staging.get(),0,D3D11_MAP_READ,0,&mapped));
        for(int y=0;y<height;++y) memcpy(pixels + y*stride, static_cast<unsigned char*>(mapped.pData)+y*mapped.RowPitch,width*4);
        context->Unmap(staging.get(),0);
    }
};

// All exports are used on one dedicated MTA worker; no GPU or permission wait touches WPF's dispatcher.
extern "C" __declspec(dllexport) HRESULT __cdecl GlassCreate(int synthetic, void** result) noexcept {
    *result = nullptr;
    HRESULT init = RoInitialize(RO_INIT_MULTITHREADED);
    if(FAILED(init)) return init;
    try {
        if(!synthetic) {
            if(!GraphicsCaptureSession::IsSupported()) throw_hresult(E_NOTIMPL);
            auto access = GraphicsCaptureAccess::RequestAccessAsync(GraphicsCaptureAccessKind::Borderless).get();
            if(access != AppCapabilityAccessStatus::Allowed) throw_hresult(E_ACCESSDENIED);
        }
        *result = new Renderer(synthetic); return S_OK;
    } catch(...) { auto hr = to_hresult(); RoUninitialize(); return hr; }
}
extern "C" __declspec(dllexport) HRESULT __cdecl GlassFrame(void* renderer,int x,int y,int w,int h,float dpi,int dark, unsigned char* pixels,int length) noexcept {
    try {
        if(!renderer || w<1 || h<1 || w>8192 || h>8192 || length < w*h*4) return E_INVALIDARG;
        auto& r = *static_cast<Renderer*>(renderer); r.Resize(w,h);
        if(r.test) {
            int aw=w+64, ah=h+64; std::vector<unsigned char> pattern(aw*ah*4);
            for(int iy=0;iy<ah;++iy) for(int ix=0;ix<aw;++ix) {
                int index=(iy*aw+ix)*4; bool stripe=((ix+x)/24+(iy+y)/24)%2==0;
                pattern[index]=stripe?245:85; pattern[index+1]=stripe?185:105; pattern[index+2]=stripe?50:235; pattern[index+3]=255;
                if(r.test == 2 || r.test == 3) pattern[index]=pattern[index+1]=pattern[index+2]=r.test==2?255:0;
                if(r.test == 4) pattern[index]=pattern[index+1]=pattern[index+2]=((ix%16)<3 && (iy%20)<12)?20:245;
            }
            r.context->UpdateSubresource(r.atlas.get(),0,nullptr,pattern.data(),aw*4,0);
        } else {
            // Clear uncovered monitor edges, never sample uninitialized GPU memory.
            std::vector<unsigned char> blank((w+64)*(h+64)*4, dark?24:240);
            r.context->UpdateSubresource(r.atlas.get(),0,nullptr,blank.data(),(w+64)*4,0);
            if(!r.Capture(RECT{x-32,y-32,x+w+32,y+h+32})) return S_FALSE;
        }
        r.Draw(float(dark),dpi,pixels,w*4); return S_OK;
    } catch(...) {return to_hresult();}
}
extern "C" __declspec(dllexport) void __cdecl GlassDestroy(void* renderer) noexcept {
    delete static_cast<Renderer*>(renderer); RoUninitialize();
}
