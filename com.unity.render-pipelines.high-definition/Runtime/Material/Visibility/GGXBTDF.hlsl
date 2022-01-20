#ifndef __GGX_BTDF_IMPORTANCE_SAMPLING_UTILS__
#define __GGX_BTDF_IMPORTANCE_SAMPLING_UTILS__

//from https://hal.inria.fr/hal-00996995v2/file/supplemental1.pdf

void SampleGGXBTDFSlopes(
    // input
    float theta_i, // incident direction
    float U1, float U2, // random numbers
    // output
    out float slope_x, out float slope_y // slopes
)
{
    // special case (normal incidence)
    if(theta_i < 0.0001)
    {
        const float r = sqrt(U1/(1-U1));
        const float phi = 6.28318530718 * U2;
        slope_x = r * cos(phi);
        slope_y = r * sin(phi);
        return;
    }
    // precomputations
    const float tan_theta_i = tan(theta_i);
    const float a = 1 / (tan_theta_i);
    const float G1 = 2 / (1 + sqrt(1.0+1.0/(a*a)));
    // sample slope_x
    const float A = 2.0*U1/G1 - 1.0;
    const float tmp = 1.0 / (A*A-1.0);
    const float B = tan_theta_i;
    const float D = sqrt(B*B*tmp*tmp - (A*A-B*B)*tmp);
    const float slope_x_1 = B*tmp - D;
    const float slope_x_2 = B*tmp + D;
    slope_x = (A < 0 || slope_x_2 > 1.0/tan_theta_i) ? slope_x_1 : slope_x_2;
    // sample slope_y
    float S;
    if(U2 > 0.5)
    {
        S = 1.0;
        U2 = 2.0*(U2-0.5);
    }
    else
    {
        S = -1.0;
        U2 = 2.0*(0.5-U2);
    }
    const float z = (U2*(U2*(U2*0.27385-0.73369)+0.46341)) / (U2*(U2*(U2*0.093073+0.309420)-1.000000)+0.597999);
    slope_y = S * z * sqrt(1.0+slope_x*slope_x);
}

void GGXBTDFImportanceSample(
    // input
    const float3 omega_i, // incident direction
    const float alpha_x, const float alpha_y, // anisotropic roughness
    const float U1, const float U2, // random numbers
    // output
    float3 omega_m) // micronormal
{
    // 1. stretch omega_i
    float3 omega_i_;
    omega_i_.x = alpha_x * omega_i.x;
    omega_i_.y = alpha_y * omega_i.y;
    omega_i_.z = omega_i.z;
    // normalize
    float inv_omega_i = 1.0 / sqrt(omega_i_.x*omega_i_.x + omega_i_.y*omega_i_.y + omega_i_.z*omega_i_.z);
    omega_i_.x *= inv_omega_i;
    omega_i_.y *= inv_omega_i;
    omega_i_.z *= inv_omega_i;
    // get polar coordinates of omega_i_
    float theta_ = 0.0;
    float phi_ = 0.0;
    if (omega_i_.z < 0.99999)
    {
        theta_ = acos(omega_i_.z);
        phi_ = atan2(omega_i_.y, omega_i_.x);
    }

    // 2. sample P22_{omega_i}(x_slope, y_slope, 1, 1)
    float slope_x, slope_y;
    SampleGGXBTDFSlopes(theta_,
        U1, U2,
        slope_x, slope_y);

    // 3. rotate
    float tmp = cos(phi_)*slope_x - sin(phi_)*slope_y;
    slope_y = sin(phi_)*slope_x + cos(phi_)*slope_y;
    slope_x = tmp;

    // 4. unstretch
    slope_x = alpha_x * slope_x;
    slope_y = alpha_y * slope_y;

    // 5. compute normal
    float inv_omega_m = sqrt(slope_x*slope_x + slope_y*slope_y + 1.0);
    omega_m.x = -slope_x/inv_omega_m;
    omega_m.y = -slope_y/inv_omega_m;
    omega_m.z = 1.0/inv_omega_m;
}



#endif
