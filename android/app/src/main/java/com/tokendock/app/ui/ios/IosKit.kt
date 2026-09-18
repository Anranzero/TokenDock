package com.tokendock.app.ui.ios

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * iOS 风格组件套件（不依赖 Material 外观）：
 * 分组内嵌列表 / 大标题 / iOS 开关 / 分段控件 / 圆环进度 / 发丝分隔线 / 分组页脚。
 * 颜色为 iOS 系统色板（浅色 + 深色两套），由 [LocalIsDark] 驱动。
 */

val LocalIsDark = compositionLocalOf { false }

/** iOS 系统色板（Light / Dark，取自 iOS 系统语义色）。 */
data class IosColors(
    val background: Color,
    val card: Color,
    val separator: Color,
    val label: Color,
    val secondaryLabel: Color,
    val tertiaryLabel: Color,
    val fill: Color,
    val accent: Color,
    val green: Color,
    val amber: Color,
    val red: Color,
)

private val IosLight = IosColors(
    background = Color(0xFFF2F2F7),
    card = Color(0xFFFFFFFF),
    separator = Color(0x293C3C43),
    label = Color(0xFF000000),
    secondaryLabel = Color(0x993C3C43),
    tertiaryLabel = Color(0x4D3C3C43),
    fill = Color(0x1F767680),
    accent = Color(0xFF0E7490),
    green = Color(0xFF34C759),
    amber = Color(0xFFFF9500),
    red = Color(0xFFFF3B30),
)

private val IosDark = IosColors(
    background = Color(0xFF000000),
    card = Color(0xFF1C1C1E),
    separator = Color(0x5454565A),
    label = Color(0xFFFFFFFF),
    secondaryLabel = Color(0x99EBEBF5),
    tertiaryLabel = Color(0x4DEBEBF5),
    fill = Color(0x24767680),
    accent = Color(0xFF22D3EE),
    green = Color(0xFF30D158),
    amber = Color(0xFFFFD60A),
    red = Color(0xFFFF453A),
)

@Composable
fun iosColors(): IosColors = if (LocalIsDark.current) IosDark else IosLight

@Composable
fun ProvideIosTheme(dark: Boolean, content: @Composable () -> Unit) {
    CompositionLocalProvider(LocalIsDark provides dark) { content() }
}

// ---------- 分组内嵌列表 ----------

/** 分组标题（iOS 大写小号灰字）。 */
@Composable
fun IosSectionHeader(text: String) {
    Text(
        text = text,
        fontSize = 13.sp,
        color = iosColors().secondaryLabel,
        modifier = Modifier.padding(start = 32.dp, top = 24.dp, bottom = 8.dp),
    )
}

/** 分组容器：白/深灰圆角卡，内部行之间用发丝线分隔。 */
@Composable
fun IosSection(content: @Composable ColumnScope.() -> Unit) {
    val colors = iosColors()
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp)
            .clip(RoundedCornerShape(12.dp))
            .background(colors.card),
        content = content,
    )
}

/** 行之间的发丝分隔线（左侧内缩，iOS 惯例）。 */
@Composable
fun IosSeparator(startInset: Dp = 16.dp) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = startInset)
            .height(1.dp)
            .background(iosColors().separator),
    )
}

/** 分组页脚说明（小号灰字，缩进与分组一致）。 */
@Composable
fun IosFooter(text: String) {
    Text(
        text = text,
        fontSize = 13.sp,
        color = iosColors().secondaryLabel,
        modifier = Modifier.padding(start = 32.dp, end = 32.dp, top = 8.dp),
    )
}

/** 通用行：可选左侧图标块 + 标题/副标题 + 右侧内容 + chevron。 */
@Composable
fun IosRow(
    title: String,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    icon: ImageVector? = null,
    iconTint: Color = iosColors().accent,
    trailingText: String? = null,
    trailing: (@Composable () -> Unit)? = null,
    showChevron: Boolean = false,
    onClick: (() -> Unit)? = null,
) {
    val colors = iosColors()
    Row(
        modifier = modifier
            .fillMaxWidth()
            .then(if (onClick != null) Modifier.clickable { onClick() } else Modifier)
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (icon != null) {
            Box(
                modifier = Modifier
                    .size(29.dp)
                    .clip(RoundedCornerShape(7.dp))
                    .background(iconTint),
                contentAlignment = Alignment.Center,
            ) {
                Icon(icon, contentDescription = null, tint = Color.White, modifier = Modifier.size(17.dp))
            }
            Spacer(Modifier.width(12.dp))
        }
        Column(Modifier.weight(1f)) {
            Text(title, fontSize = 17.sp, color = colors.label)
            if (subtitle != null) {
                Text(subtitle, fontSize = 13.sp, color = colors.secondaryLabel)
            }
        }
        if (trailingText != null) {
            Text(trailingText, fontSize = 17.sp, color = colors.secondaryLabel)
        }
        trailing?.invoke()
        if (showChevron) {
            Spacer(Modifier.width(6.dp))
            Text("›", fontSize = 20.sp, color = colors.tertiaryLabel)
        }
    }
}

// ---------- iOS 开关 ----------

@Composable
fun IosSwitch(
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    enabled: Boolean = true,
) {
    val colors = iosColors()
    val trackWidth = 51.dp
    val trackHeight = 31.dp
    val thumbSize = 27.dp
    val offset by animateDpAsState(
        targetValue = if (checked) trackWidth - thumbSize - 2.dp else 2.dp,
        animationSpec = tween(180),
        label = "iosSwitchThumb",
    )
    val track by animateColorAsState(
        targetValue = when {
            !enabled -> colors.fill
            checked -> colors.green
            else -> colors.fill
        },
        label = "iosSwitchTrack",
    )

    Box(
        modifier = Modifier
            .size(trackWidth, trackHeight)
            .clip(CircleShape)
            .background(track)
            .clickable(enabled = enabled) { onCheckedChange(!checked) },
        contentAlignment = Alignment.CenterStart,
    ) {
        Box(
            modifier = Modifier
                .offset(x = offset)
                .size(thumbSize)
                .shadow(1.dp, CircleShape)
                .clip(CircleShape)
                .background(Color.White),
        )
    }
}

// ---------- iOS 分段控件 ----------

@Composable
fun IosSegmentedControl(
    options: List<String>,
    selectedIndex: Int,
    onSelected: (Int) -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = iosColors()
    val thumbOffset by animateDpAsState(
        targetValue = (selectedIndex * (1f / options.size) * 100).dp,
        animationSpec = tween(180),
        label = "segmentThumb",
    )

    Box(
        modifier = modifier
            .fillMaxWidth()
            .height(32.dp)
            .clip(RoundedCornerShape(9.dp))
            .background(colors.fill)
            .padding(2.dp),
    ) {
        // 滑动白色指示块（宽度按 1/N，位置用百分比偏移）
        Box(
            modifier = Modifier
                .fillMaxWidth(1f / options.size)
                .height(28.dp)
                .offset(x = thumbOffset)
                .shadow(2.dp, RoundedCornerShape(7.dp))
                .clip(RoundedCornerShape(7.dp))
                .background(colors.card),
        )
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            options.forEachIndexed { index, label ->
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(7.dp))
                        .clickable(
                            interactionSource = remember { MutableInteractionSource() },
                            indication = null,
                        ) { onSelected(index) },
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = label,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = if (index == selectedIndex) colors.label else colors.secondaryLabel,
                        textAlign = TextAlign.Center,
                    )
                }
            }
        }
    }
}

// ---------- 圆环进度 ----------

@Composable
fun IosRing(
    progress: Float?,
    color: Color,
    modifier: Modifier = Modifier,
    size: Dp = 52.dp,
    stroke: Dp = 6.dp,
    content: @Composable () -> Unit,
) {
    val colors = iosColors()
    val animated by animateFloatAsState(
        targetValue = (progress ?: 0f).coerceIn(0f, 1f),
        animationSpec = tween(450),
        label = "ringProgress",
    )
    Box(modifier = modifier.size(size), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxWidth().height(size)) {
            val strokePx = stroke.toPx()
            val inset = strokePx / 2f
            val arcSize = Size(this.size.width - strokePx, this.size.height - strokePx)
            drawArc(
                color = colors.fill,
                startAngle = 0f,
                sweepAngle = 360f,
                useCenter = false,
                topLeft = Offset(inset, inset),
                size = arcSize,
                style = Stroke(width = strokePx),
            )
            if (progress != null && animated > 0f) {
                drawArc(
                    color = color,
                    startAngle = -90f,
                    sweepAngle = 360f * animated,
                    useCenter = false,
                    topLeft = Offset(inset, inset),
                    size = arcSize,
                    style = Stroke(width = strokePx, cap = StrokeCap.Round),
                )
            }
        }
        content()
    }
}

/** 分组内的内容块（用于分段控件、输入框等整行内容）。 */
@Composable
fun IosRowBlock(content: @Composable () -> Unit) {
    Box(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp)) { content() }
}

/** 行内水平排列辅助（标题 + 右侧值）。 */
@Composable
fun IosLabeledRow(title: String, trailing: @Composable () -> Unit) {
    val colors = iosColors()
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(title, fontSize = 17.sp, color = colors.label)
        trailing()
    }
}
