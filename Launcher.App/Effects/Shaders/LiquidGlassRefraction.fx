sampler2D inputSampler : register(s0);

float edgeX : register(c0);
float edgeY : register(c1);
float normalX : register(c2);
float normalY : register(c3);
float aspectRatio : register(c4);
float intensity : register(c5);
float refractionRadius : register(c6);
float distortionAmount : register(c7);
float phase : register(c8);
float highlightGain : register(c9);
float restingRefraction : register(c10);
float cornerRadius : register(c11);

float SmoothUnit(float value)
{
    value = saturate(value);
    return value * value * (3.0 - (2.0 * value));
}

float2 ClampSampleCoordinate(float2 uv)
{
    return clamp(uv, float2(0.001, 0.001), float2(0.999, 0.999));
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float aspect = max(aspectRatio, 0.001);
    float radius = max(refractionRadius, 0.001);
    float activeIntensity = saturate(intensity);
    float2 edge = float2(edgeX, edgeY);
    float2 normal = normalize(float2(normalX, normalY) + float2(0.00001, 0.00001));
    float2 tangent = float2(-normal.y, normal.x);
    float2 metricScale = float2(aspect, 1.0);
    float2 relative = (uv - edge) * metricScale;
    float normalDistance = dot(relative, normal);
    float tangentDistance = dot(relative, tangent);

    float tangentialEnvelope = SmoothUnit(1.0 - (abs(tangentDistance) / radius));
    float normalSpan = max(radius * 0.18, 0.001);
    float normalEnvelope = SmoothUnit(1.0 - (abs(normalDistance) / normalSpan));
    float envelope = tangentialEnvelope * normalEnvelope * activeIntensity;

    float wavePhase = ((tangentDistance / radius) * 10.4) + phase;
    float wave = sin(wavePhase);
    float curvature = 1.0 - saturate(abs(normalDistance) / normalSpan);
    float2 normalUv = float2(normal.x / aspect, normal.y);
    float2 tangentUv = float2(tangent.x / aspect, tangent.y);
    float signedBulge = 0.52 - saturate(normalDistance / normalSpan);
    float displacement = distortionAmount * envelope;
    float2 offset = normalUv * displacement * signedBulge * (0.78 + (0.22 * wave));
    offset += tangentUv * displacement * 0.07 * sin((wavePhase * 0.73) + 1.2);

    // A low-amplitude rounded-box lens remains visible at rest, so compact
    // controls read as glass before the pointer reaches their edge.
    float2 local = (uv - float2(0.5, 0.5)) * metricScale;
    float2 halfExtent = float2(aspect * 0.5, 0.5);
    float roundedRadius = clamp(cornerRadius, 0.001, 0.5);
    float2 q = abs(local) - max(halfExtent - roundedRadius, float2(0.001, 0.001));
    float2 outsideQ = max(q, float2(0.0, 0.0));
    float signedDistance = length(outsideQ) + min(max(q.x, q.y), 0.0) - roundedRadius;
    float boundaryDistance = max(-signedDistance, 0.0);
    float2 localSign = float2(local.x >= 0.0 ? 1.0 : -1.0, local.y >= 0.0 ? 1.0 : -1.0);
    float2 outwardNormal;
    if (outsideQ.x > 0.00001 || outsideQ.y > 0.00001)
        outwardNormal = normalize(outsideQ + float2(0.00001, 0.00001)) * localSign;
    else if (q.x > q.y)
        outwardNormal = float2(localSign.x, 0.0);
    else
        outwardNormal = float2(0.0, localSign.y);

    float2 restingNormal = -outwardNormal;
    float2 restingNormalUv = float2(restingNormal.x / aspect, restingNormal.y);
    float2 restingTangentUv = float2(-restingNormal.y / aspect, restingNormal.x);
    float restingSpan = max(roundedRadius * 0.48, 0.028);
    float restingProgress = saturate(boundaryDistance / restingSpan);
    float restingEnvelope = SmoothUnit(1.0 - restingProgress) * saturate(restingRefraction);
    float restingBulge = 0.52 - (restingProgress * 0.76);
    float restingSuppression = 1.0 - (envelope * 0.85);
    float restingLens = restingEnvelope * restingSuppression;
    offset += restingNormalUv * distortionAmount * restingLens * restingBulge * 0.82;

    float combinedEnvelope = max(envelope, restingLens);
    float2 spectralDirection = restingLens > envelope ? restingTangentUv : tangentUv;
    float chromaticShift = distortionAmount * combinedEnvelope * (0.015 + (0.025 * activeIntensity));
    float2 sampleUv = ClampSampleCoordinate(uv + offset);
    float4 originalSample = tex2D(inputSampler, ClampSampleCoordinate(uv));
    float4 centerSample = tex2D(inputSampler, sampleUv);
    float red = tex2D(inputSampler, ClampSampleCoordinate(sampleUv + (spectralDirection * chromaticShift))).r;
    float blue = tex2D(inputSampler, ClampSampleCoordinate(sampleUv - (spectralDirection * chromaticShift))).b;
    float3 refracted = float3(red, centerSample.g, blue);

    float ridgeWidth = max(normalSpan * 0.11, 0.001);
    float ridge = SmoothUnit(1.0 - (abs(normalDistance) / ridgeWidth));
    ridge *= tangentialEnvelope * activeIntensity;
    float grazing = SmoothUnit(curvature) * tangentialEnvelope * activeIntensity;
    float restingRidgeWidth = max(restingSpan * 0.2, 0.001);
    float restingRidge = SmoothUnit(1.0 - (boundaryDistance / restingRidgeWidth)) * saturate(restingRefraction);
    float2 materialLight = normalize(float2(-0.55, -0.83));
    float lightFacing = saturate((dot(outwardNormal, materialLight) * 0.5) + 0.5);
    float restingSpecularGain = lerp(0.018, 0.060, SmoothUnit(lightFacing));
    float restingBackShade = restingRidge * lerp(0.028, 0.006, SmoothUnit(lightFacing));
    float specular = (ridge * 0.08)
        + (grazing * 0.012 * (0.5 + (0.5 * wave)))
        + (restingRidge * restingSuppression * restingSpecularGain);
    refracted += specular * max(highlightGain, 0.0);
    refracted = max(refracted - (restingBackShade * restingSuppression), float3(0.0, 0.0, 0.0));

    float luminance = dot(refracted, float3(0.2126, 0.7152, 0.0722));
    refracted = lerp(refracted, float3(luminance, luminance, luminance), combinedEnvelope * 0.12);
    float activeAlpha = (envelope * 0.42) + (ridge * 0.18);
    float restingAlpha = (restingLens * 0.34)
        + (restingRidge * restingSuppression * 0.12);
    float layerAlpha = originalSample.a * saturate(max(activeAlpha, restingAlpha));
    return float4(refracted * layerAlpha, layerAlpha);
}
