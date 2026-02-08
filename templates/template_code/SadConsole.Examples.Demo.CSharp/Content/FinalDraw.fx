#if OPENGL
	#define VS_SHADERMODEL vs_4_0
	#define PS_SHADERMODEL ps_4_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture : register(t0);
SamplerState SpriteTextureSampler : register(s0);

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color : COLOR0;
	float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : SV_TARGET
{
	float4 color = SpriteTexture.Sample(SpriteTextureSampler, input.TextureCoordinates) * input.Color;
	color.gb = color.r;
	return color;
}

technique SpriteDrawing
{
	pass P0
	{
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};