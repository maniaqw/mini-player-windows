// Rounded glass SDF with a traveling light and refracted caustic streaks.
// fxc /T ps_3_0 /E main /Fo Shaders/Glass.ps Shaders/Glass.hlsl
float time : register(c0);
float2 size : register(c1);
float4 tint : register(c2);
float4 lightTint : register(c3);
float liquid : register(c4);
float2 pointer : register(c5);

float2 perimeterPoint(float travel, float2 halfSize, float radius)
{
    float x = halfSize.x - radius;
    float y = halfSize.y - radius;
    float arc = 1.5707963268 * radius;
    float length = 4 * (x + y) + 4 * arc;
    float s = frac(travel) * length;
    if (s < 2*x) return float2(-x+s, -halfSize.y);
    s -= 2*x;
    if (s < arc) { float a = -1.5707963268+s/radius; return float2(x,-y)+radius*float2(cos(a),sin(a)); }
    s -= arc;
    if (s < 2*y) return float2(halfSize.x,-y+s);
    s -= 2*y;
    if (s < arc) { float a=s/radius; return float2(x,y)+radius*float2(cos(a),sin(a)); }
    s -= arc;
    if (s < 2*x) return float2(x-s,halfSize.y);
    s -= 2*x;
    if (s < arc) { float a=1.5707963268+s/radius; return float2(-x,y)+radius*float2(cos(a),sin(a)); }
    s -= arc;
    if (s < 2*y) return float2(-halfSize.x,y-s);
    s -= 2*y;
    float a=3.1415926536+s/radius;
    return float2(-x,-y)+radius*float2(cos(a),sin(a));
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 p = (uv-0.5)*size;
    float2 h = size*0.5-12;
    float radius = lerp(30, min(58, h.y), liquid);
    float2 q = abs(p)-(h-radius);
    float distance = length(max(q,0)) + min(max(q.x,q.y),0)-radius;
    float inside = 1-smoothstep(-0.7,0.7,distance);
    if (liquid > 0.5)
    {
        float depth = max(0, -distance);
        float2 cornerCenter = clamp(p, -h+radius, h-radius);
        float2 normal = normalize(p-cornerCenter+float2(0.0001,0.0001));
        float2 lamp = (pointer-0.5)*size;
        float focus = exp(-dot((p-lamp)/float2(180,130),(p-lamp)/float2(180,130)));
        float rim = exp(-abs(distance+1.2)*1.1);
        float lens = exp(-depth/8.0) * inside;
        float specular = pow(saturate(dot(normal,normalize(float2(-0.5,-0.85)))),7);
        float opposite = pow(saturate(dot(normal,normalize(float2(0.55,0.8)))),9);
        float bevel = lens * (0.08+specular*0.38+opposite*0.19);
        float sheen = pow(saturate(1-uv.y),4)*0.075;
        float hover = focus*0.06;
        float isLight = step(0.6,dot(tint.rgb,float3(0.2126,0.7152,0.0722)));
        float bodyOpacity = lerp(0.68,0.65,isLight);
        float alpha = inside*(bodyOpacity+sheen+hover+bevel)+rim*0.44;
        float3 neutral = lerp(lerp(float3(0.12,0.14,0.17),tint.rgb,0.25),float3(0.96,0.975,0.99),isLight);
        float3 color = neutral*inside*bodyOpacity + float3(0.92,0.97,1)*(inside*(sheen+hover+bevel)+rim*0.44);
        return float4(min(color,alpha),saturate(alpha));
    }
    float edge = exp(-abs(distance+1.4)*0.65);
    float corners = smoothstep(h.x-radius-6,h.x-radius+2,abs(p.x)) * smoothstep(h.y-radius-6,h.y-radius+2,abs(p.y));
    float bracket = (1-smoothstep(3.4,5.4,abs(distance+2.0))) * corners;
    float2 light = perimeterPoint(time/6.8+0.08,h,radius);
    float2 light2 = perimeterPoint(time/6.8+0.57,h,radius);
    float head = exp(-dot(p-light,p-light)/7800);
    float tail = exp(-dot(p-light2,p-light2)/5000)*0.6;
    float illumination = head+tail;
    float stripe = pow(saturate(0.5+0.5*sin((p.x+p.y*2.4)*0.29-time*5.0+sin(p.x*0.024+p.y*0.012)*3.5)),14);
    float refract = stripe * illumination * exp(-abs(distance+3)/9.5) * (1-smoothstep(25,38,-distance)) * inside;
    float glow = exp(-abs(distance)*0.19)*0.16;
    float4 body = float4(tint.rgb*(0.91+uv.y*0.09),inside);
    float3 lightColor = lightTint.rgb;
    float lightAlpha = edge*(0.22+illumination*0.68) + bracket*0.48 + refract*0.68;
    float alpha = saturate(body.a+glow*(0.5+illumination));
    float3 color = body.rgb*body.a + lightColor*glow*0.4 + lightColor*lightAlpha;
    return float4(min(color,alpha),alpha);
}
